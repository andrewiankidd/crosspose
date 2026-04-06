using System.Collections.Concurrent;
using System.Text.Json;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Detects Podman containers that are stuck in a restart loop due to the rootless
/// network-namespace stale-state issue (container created before its dependency was
/// ready, then restart replays the broken state rather than retrying fresh).
///
/// When AutoFix fires: removes the container and re-runs compose up --force-recreate
/// for its project so the container is rebuilt with a fresh network context and
/// depends_on ordering is re-evaluated.
///
/// Fibonacci cooldown (1→1→2 min) with a 3-attempt cap prevents runaway healing on
/// genuinely broken containers.
/// </summary>
public sealed class PodmanContainerAutohealCheck : ICheckFix
{
    // Fibonacci cooldown in minutes between autoheal attempts: 1, 2, 3, 5, 8
    // Gives up to ~19 minutes of healing window — enough for slow DB schema migrations.
    private static readonly int[] CooldownMinutes = [1, 2, 3, 5, 8];
    private const int MaxAttempts = 5;
    // Not used for gating — any non-zero-exit stopped/exited container is eligible.
    // Kept for reference: podman restart counts escalate slowly so we can't rely on them as the sole signal.

    private sealed record AutohealEntry(int Attempts, DateTime NextEligible, bool Abandoned);
    private readonly ConcurrentDictionary<string, AutohealEntry> _state = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "podman-autoheal";
    public string Description => "Detects Podman containers stuck in a restart loop and recreates them via compose up.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 30;
    public IReadOnlyList<string> AutoFixRequires => ["wsl", "podman-wsl", "podman-cgroups"];

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var stuck = await GetStuckContainersAsync(runner, cancellationToken).ConfigureAwait(false);

        // Clear autoheal state for containers that have recovered
        foreach (var key in _state.Keys.ToList())
        {
            if (!stuck.Any(c => string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase)))
                _state.TryRemove(key, out _);
        }

        if (stuck.Count == 0)
            return CheckResult.Success("No Podman containers stuck in restart loop.");

        var names = string.Join(", ", stuck.Select(c => c.Name));
        return CheckResult.Failure($"{stuck.Count} container(s) stuck in restart loop: {names}");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var stuck = await GetStuckContainersAsync(runner, cancellationToken).ConfigureAwait(false);
        if (stuck.Count == 0)
            return FixResult.Success("No containers need healing.");

        var healed = 0;
        var skipped = 0;
        var failed = new List<string>();

        foreach (var container in stuck)
        {
            var entry = _state.GetOrAdd(container.Name, _ => new AutohealEntry(0, DateTime.UtcNow, false));

            if (entry.Abandoned)
            {
                logger.LogWarning("Autoheal abandoned for {Name} after {Max} attempts — likely a genuine failure.", container.Name, MaxAttempts);
                skipped++;
                continue;
            }

            if (DateTime.UtcNow < entry.NextEligible)
            {
                skipped++;
                continue;
            }

            logger.LogInformation("Autohealing {Name} (attempt {Attempt}/{Max})", container.Name, entry.Attempts + 1, MaxAttempts);

            var wsl = new WslRunner(runner);
            var distro = CrossposeEnvironment.WslDistro;

            // Remove the stale container
            var rm = await wsl.ExecAsync(
                ["-d", distro, "--", "podman", "rm", "-f", container.Id],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!rm.IsSuccess)
            {
                logger.LogWarning("Failed to remove container {Name}: {Error}", container.Name, rm.StandardError);
                failed.Add(container.Name);
                continue;
            }

            // Find the deployment directory for this project
            var deployDir = FindDeploymentDir(container.Project);
            if (deployDir is null)
            {
                logger.LogWarning("No deployment directory found for project '{Project}' (container {Name})", container.Project, container.Name);
                failed.Add(container.Name);
                continue;
            }

            // Include both linux workload files and infra files (mssql, emulators).
            // Infra files define the services that app containers depend_on — without them
            // podman-compose can't resolve service_healthy dependencies and exits 1.
            var composeFiles = Directory.GetFiles(deployDir, "docker-compose.*.yml", SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(".windows.yml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .Select(f => $"-f \"{ToWslPath(f)}\"")
                .ToList();

            if (composeFiles.Count == 0)
            {
                logger.LogWarning("No Linux compose files found in {Dir} for project '{Project}'", deployDir, container.Project);
                failed.Add(container.Name);
                continue;
            }

            var fileArgs = string.Join(" ", composeFiles);
            var projectArg = string.IsNullOrWhiteSpace(container.Project) ? "" : $"-p \"{container.Project}\"";
            // Container was already removed above. Run plain "up -d" (no --force-recreate):
            // podman-compose will see the container is absent and create+start only the missing
            // one, leaving all other running containers untouched and respecting depends_on.
            var composeCmd = $"podman-compose {projectArg} {fileArgs} up -d";

            var up = await wsl.ExecAsync(
                ["-d", distro, "--", "sh", "-c", $"\"{composeCmd}\""],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var nextAttempt = entry.Attempts + 1;
            var abandoned = nextAttempt >= MaxAttempts;
            var cooldownIdx = Math.Min(nextAttempt, CooldownMinutes.Length - 1);
            _state[container.Name] = new AutohealEntry(nextAttempt, DateTime.UtcNow.AddMinutes(CooldownMinutes[cooldownIdx]), abandoned);

            if (up.IsSuccess)
            {
                logger.LogInformation("Autohealed {Name} — compose up succeeded.", container.Name);
                healed++;
            }
            else
            {
                logger.LogWarning("Autoheal compose up failed for {Name}: {Error}", container.Name, up.StandardError);
                failed.Add(container.Name);
            }
        }

        if (healed > 0)
            return FixResult.Success($"Healed {healed} container(s). Skipped (cooldown/abandoned): {skipped}. Failed: {failed.Count}.");

        if (failed.Count > 0)
            return FixResult.Failure($"No containers healed. Failed: {string.Join(", ", failed)}. Skipped: {skipped}.");

        return FixResult.Success($"No action taken — {skipped} container(s) in cooldown or abandoned.");
    }

    private sealed record StuckContainer(string Name, string Id, string? Project, int RestartCount);

    private static async Task<IReadOnlyList<StuckContainer>> GetStuckContainersAsync(
        ProcessRunner runner, CancellationToken cancellationToken)
    {
        var wsl = new WslRunner(runner);
        var distro = CrossposeEnvironment.WslDistro;
        var result = await wsl.ExecAsync(
            ["-d", distro, "--", "podman", "ps", "-a", "--format", "json"],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
            return [];

        var stuck = new List<StuckContainer>();
        try
        {
            using var doc = JsonDocument.Parse(result.StandardOutput);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return [];

            foreach (var item in root.EnumerateArray())
            {
                // Only containers that stopped/exited with a non-zero code
                // podman ps --format json uses "stopped" when restart policy is exhausted, "exited" while actively restarting
                var state = item.TryGetProperty("State", out var s) ? s.GetString() : null;
                if (!string.Equals(state, "exited", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(state, "stopped", StringComparison.OrdinalIgnoreCase))
                    continue;

                var exitCode = item.TryGetProperty("ExitCode", out var ec) && ec.TryGetInt32(out var ecv) ? ecv : 0;
                if (exitCode == 0) continue;

                var restartCount = item.TryGetProperty("Restarts", out var rc) && rc.TryGetInt32(out var rcv) ? rcv : 0;

                var name = item.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array
                    ? names.EnumerateArray().FirstOrDefault().GetString()
                    : item.TryGetProperty("Name", out var n) ? n.GetString() : null;

                var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;

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

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;

                stuck.Add(new StuckContainer(name, id, project, restartCount));
            }
        }
        catch
        {
            // If JSON parsing fails, return empty — don't break the check cycle
        }

        return stuck;
    }

    /// <summary>
    /// Extracts the compose service name from a container name of the form
    /// <c>&lt;project&gt;_&lt;service&gt;_&lt;index&gt;</c> or <c>&lt;project&gt;-&lt;service&gt;-&lt;index&gt;</c>.
    /// Returns null if the service name cannot be determined.
    /// </summary>
    private static string? ExtractServiceName(string containerName, string? project)
    {
        if (string.IsNullOrWhiteSpace(containerName)) return null;

        // Strip project prefix if present
        var body = containerName;
        if (!string.IsNullOrWhiteSpace(project))
        {
            var prefix = project + "_";
            if (body.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                body = body[prefix.Length..];
        }

        // Strip trailing _<digits> or -<digits>
        var lastSep = body.LastIndexOfAny(['_', '-']);
        if (lastSep > 0 && body[(lastSep + 1)..].All(char.IsDigit))
            body = body[..lastSep];

        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    /// <summary>
    /// Finds the version subdirectory for the given compose project name within the deployment root.
    /// Looks for DeploymentDirectory/&lt;project&gt;/&lt;version&gt;/ where compose files exist.
    ///
    /// When the project label matches a version name (e.g. "default" — which is the version subdir
    /// name used by compose when ProjectName isn't propagated), falls back to scanning all project
    /// dirs for a version subdir whose name matches the label.
    /// </summary>
    private static string? FindDeploymentDir(string? project)
    {
        if (string.IsNullOrWhiteSpace(project)) return null;

        var deployRoot = CrossposeEnvironment.DeploymentDirectory;
        if (!Directory.Exists(deployRoot)) return null;

        // Primary: label is the outer project name — find its version subdir with linux files
        var projectDir = Path.Combine(deployRoot, project);
        if (Directory.Exists(projectDir))
        {
            var versionDir = Directory.GetDirectories(projectDir)
                .OrderByDescending(Directory.GetLastWriteTime)
                .FirstOrDefault(dir =>
                    Directory.GetFiles(dir, "docker-compose.*.linux.yml", SearchOption.TopDirectoryOnly).Length > 0);
            if (versionDir is not null) return versionDir;
        }

        // Fallback: label equals the version subdir name (e.g. "default") — scan all project dirs
        // for a version subdir matching this name that contains linux compose files.
        foreach (var outerDir in Directory.GetDirectories(deployRoot).OrderByDescending(Directory.GetLastWriteTime))
        {
            var candidate = Path.Combine(outerDir, project);
            if (Directory.Exists(candidate) &&
                Directory.GetFiles(candidate, "docker-compose.*.linux.yml", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Converts a Windows absolute path to its /mnt/<drive>/... WSL equivalent.</summary>
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
