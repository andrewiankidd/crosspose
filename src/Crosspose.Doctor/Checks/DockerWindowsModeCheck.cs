using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Validates that Docker Desktop is switched to Windows containers (required for Windows compose stacks).
/// </summary>
public sealed class DockerWindowsModeCheck : ICheckFix
{
    public string Name => "docker-windows-mode";
    public string Description => "Verifies Docker Desktop is running Windows containers.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var probe = await runner.RunAsync("docker", "info --format \"{{.OSType}}\"", cancellationToken: cancellationToken);
        if (!probe.IsSuccess)
        {
            var probeError = string.IsNullOrWhiteSpace(probe.StandardError)
                ? "Unable to query docker info. Is Docker Desktop running?"
                : probe.StandardError.Trim().Split(Environment.NewLine)[0];
            return CheckResult.Failure(probeError);
        }

        var osType = ExtractOsType(probe.StandardOutput);
        if (string.Equals(osType, "windows", StringComparison.OrdinalIgnoreCase))
        {
            return CheckResult.Success("Docker Desktop is running Windows containers.");
        }

        var mode = string.IsNullOrWhiteSpace(osType) ? "unknown" : osType;
        return CheckResult.Failure($"Docker Desktop is targeting '{mode}' containers. Switch to Windows containers.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var dockerCli = ResolveDockerCliPath();
        if (dockerCli is null)
        {
            return FixResult.Failure("DockerCli.exe not found. Switch to Windows containers from the Docker Desktop tray menu.");
        }

        var switchResult = await runner.RunAsync(dockerCli, "-SwitchWindowsEngine", cancellationToken: cancellationToken);
        if (!switchResult.IsSuccess)
        {
            var switchError = string.IsNullOrWhiteSpace(switchResult.StandardError) ? switchResult.StandardOutput : switchResult.StandardError;
            switchError = string.IsNullOrWhiteSpace(switchError) ? "Unknown error switching Docker Desktop mode." : switchError.Trim();
            return FixResult.Failure(switchError);
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var validation = await runner.RunAsync("docker", "info --format \"{{.OSType}}\"", cancellationToken: cancellationToken);
            if (!validation.IsSuccess) continue;

            var osType = ExtractOsType(validation.StandardOutput);
            if (string.Equals(osType, "windows", StringComparison.OrdinalIgnoreCase))
            {
                return FixResult.Success("Docker Desktop switched to Windows containers.");
            }
        }

        return FixResult.Failure("Docker Desktop did not report Windows containers in time. Please switch manually.");
    }

    private static string? ExtractOsType(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.IndexOf(':') is var idx && idx > -1)
            {
                var key = trimmed[..idx].Trim();
                if (key.Equals("OSType", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[(idx + 1)..].Trim();
                }
            }
            else
            {
                return trimmed;
            }
        }

        return null;
    }

    private static string? ResolveDockerCliPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "DockerCli.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Docker", "Docker", "DockerCli.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fall back to PATH lookup
        var path = CrossposeEnvironment.Path;
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var cli = Path.Combine(segment.Trim(), "DockerCli.exe");
            if (File.Exists(cli))
            {
                return cli;
            }
        }

        return null;
    }
}
