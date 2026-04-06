using System.Text.Json;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Detects podman containers stuck in Created state — created by podman-compose but not yet
/// started because their depends_on: service_healthy dependency hadn't become healthy yet.
/// Once the dependency transitions to healthy (driven by PodmanHealthcheckRunnerCheck), this
/// check fires FixAsync which re-runs `podman-compose up -d` for the project, allowing
/// compose to re-evaluate the dependency conditions and start the waiting containers.
/// </summary>
public sealed class PodmanCreatedContainerCheck : ICheckFix
{
    public string Name => "podman-created-container";
    public string Description => "Starts podman containers stuck in Created state once their healthy dependencies are ready.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 15;
    public IReadOnlyList<string> AutoFixRequires => ["wsl", "podman-wsl", "podman-cgroups"];

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var created = await GetCreatedContainersAsync(runner, cancellationToken).ConfigureAwait(false);
        if (created.Count == 0)
            return CheckResult.Success("No podman containers stuck in Created state.");

        var names = string.Join(", ", created.Select(c => c.Name));
        return CheckResult.Failure($"{created.Count} container(s) stuck in Created state: {names}");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var created = await GetCreatedContainersAsync(runner, cancellationToken).ConfigureAwait(false);
        if (created.Count == 0)
            return FixResult.Success("No Created containers to start.");

        // Group by project and run compose up once per project.
        var projects = created
            .Select(c => c.Project)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var started = 0;
        var failed = new List<string>();
        var wsl = new WslRunner(runner);
        var distro = CrossposeEnvironment.WslDistro;

        foreach (var project in projects)
        {
            var deployDir = FindDeploymentDir(project!);
            if (deployDir is null)
            {
                logger.LogWarning("No deployment directory found for project '{Project}'", project);
                failed.Add(project!);
                continue;
            }

            var composeFiles = Directory.GetFiles(deployDir, "docker-compose.*.linux.yml", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .Select(f => $"-f \"{ToWslPath(f)}\"")
                .ToList();

            if (composeFiles.Count == 0)
            {
                logger.LogWarning("No Linux compose files found in {Dir} for project '{Project}'", deployDir, project);
                failed.Add(project!);
                continue;
            }

            var fileArgs = string.Join(" ", composeFiles);
            var projectArg = $"-p \"{project}\"";
            var composeCmd = $"podman-compose --podman-run-args=--replace {projectArg} {fileArgs} up -d";

            logger.LogInformation("Starting Created containers for project '{Project}' via compose up.", project);

            var up = await wsl.ExecAsync(
                ["-d", distro, "--", "sh", "-c", composeCmd],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (up.IsSuccess)
            {
                logger.LogInformation("Compose up succeeded for project '{Project}'.", project);
                started++;
            }
            else
            {
                logger.LogWarning("Compose up failed for project '{Project}': {Error}", project, up.StandardError);
                failed.Add(project!);
            }
        }

        if (started > 0 && failed.Count == 0)
            return FixResult.Success($"Started containers for {started} project(s).");

        if (failed.Count > 0)
            return FixResult.Failure($"Failed to start containers for: {string.Join(", ", failed)}");

        return FixResult.Success("No action taken.");
    }

    private sealed record CreatedContainer(string Name, string? Project);

    private static async Task<IReadOnlyList<CreatedContainer>> GetCreatedContainersAsync(
        ProcessRunner runner, CancellationToken cancellationToken)
    {
        var wsl = new WslRunner(runner);
        var distro = CrossposeEnvironment.WslDistro;

        var result = await wsl.ExecAsync(
            ["-d", distro, "--", "podman", "ps", "-a", "--filter", "status=created", "--format", "json"],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
            return [];

        var containers = new List<CreatedContainer>();
        try
        {
            using var doc = JsonDocument.Parse(result.StandardOutput);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? name = null;
                if (item.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array)
                    name = names.EnumerateArray().FirstOrDefault().GetString();
                else if (item.TryGetProperty("Name", out var n))
                    name = n.GetString();

                if (string.IsNullOrWhiteSpace(name)) continue;

                string? project = null;
                if (item.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
                {
                    foreach (var label in labels.EnumerateObject())
                    {
                        if (label.NameEquals("com.docker.compose.project"))
                        {
                            project = label.Value.GetString();
                            break;
                        }
                    }
                }

                containers.Add(new CreatedContainer(name, project));
            }
        }
        catch { }

        return containers;
    }

    private static string? FindDeploymentDir(string project)
    {
        var deployRoot = CrossposeEnvironment.DeploymentDirectory;
        if (!Directory.Exists(deployRoot)) return null;

        // Primary: label is the outer project name.
        var projectDir = Path.Combine(deployRoot, project);
        if (Directory.Exists(projectDir))
        {
            var versionDir = Directory.GetDirectories(projectDir)
                .OrderByDescending(Directory.GetLastWriteTime)
                .FirstOrDefault(dir =>
                    Directory.GetFiles(dir, "docker-compose.*.linux.yml", SearchOption.TopDirectoryOnly).Length > 0);
            if (versionDir is not null) return versionDir;
        }

        // Fallback: label equals the version subdir name (e.g. "default").
        foreach (var outerDir in Directory.GetDirectories(deployRoot).OrderByDescending(Directory.GetLastWriteTime))
        {
            var candidate = Path.Combine(outerDir, project);
            if (Directory.Exists(candidate) &&
                Directory.GetFiles(candidate, "docker-compose.*.linux.yml", SearchOption.TopDirectoryOnly).Length > 0)
                return candidate;
        }

        return null;
    }

    private static string ToWslPath(string windowsPath)
    {
        windowsPath = Path.GetFullPath(windowsPath);
        if (windowsPath.Length >= 2 && windowsPath[1] == ':')
        {
            var drive = char.ToLowerInvariant(windowsPath[0]);
            var rest = windowsPath[2..].Replace('\\', '/').TrimStart('/');
            return $"/mnt/{drive}/{rest}";
        }
        return windowsPath.Replace('\\', '/');
    }
}
