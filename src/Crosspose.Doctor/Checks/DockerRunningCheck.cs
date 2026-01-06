using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Crosspose.Doctor.Checks;

public sealed class DockerRunningCheck : ICheckFix
{
    public string Name => "docker-running";
    public string Description => "Verifies Docker engine is running and reachable.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("docker", "info", cancellationToken: cancellationToken);
        if (result.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? "Docker engine is running."
                : result.StandardOutput.Split(Environment.NewLine).First();
            return CheckResult.Success(message);
        }

        var error = string.IsNullOrWhiteSpace(result.StandardError)
            ? "Docker engine not reachable. Is Docker Desktop running?"
            : result.StandardError.Split(Environment.NewLine).First();
        return CheckResult.Failure(error);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var exe = ResolveDockerDesktopPath();
        if (exe is null)
        {
            return FixResult.Failure("Docker Desktop executable not found. Please start Docker manually.");
        }

        var ps = $"Start-Process -FilePath '{exe}'";
        var start = await runner.RunAsync("powershell", $"-NoProfile -Command \"{ps}\"", cancellationToken: cancellationToken);
        if (!start.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(start.StandardError)
                ? "Failed to start Docker Desktop."
                : start.StandardError.Split(Environment.NewLine).First();
            return FixResult.Failure(error);
        }

        // Wait briefly for the engine to come up.
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var probe = await runner.RunAsync("docker", "info", cancellationToken: cancellationToken);
            if (probe.IsSuccess)
            {
                return FixResult.Success("Docker Desktop started and engine is responding.");
            }
        }

        return FixResult.Failure("Started Docker Desktop, but the engine did not respond in time.");
    }

    private static string? ResolveDockerDesktopPath()
    {
        // Common install locations
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Docker", "Docker", "Docker Desktop.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // Search for docker.exe on PATH and walk up to Docker Desktop.exe
        var path = CrossposeEnvironment.Path;
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dockerExe = Path.Combine(segment.Trim(), "docker.exe");
            if (!File.Exists(dockerExe)) continue;

            var dir = Path.GetDirectoryName(dockerExe);
            if (dir is null) continue;

            var parent = Directory.GetParent(dir);
            var grandParent = parent?.Parent;
            if (grandParent is null) continue;

            var desktop = Path.Combine(grandParent.FullName, "Docker Desktop.exe");
            if (File.Exists(desktop)) return desktop;
        }

        return null;
    }
}
