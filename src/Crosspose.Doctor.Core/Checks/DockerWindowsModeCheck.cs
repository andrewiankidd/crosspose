using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Crosspose.Doctor.Core.Checks;

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
        var context = await ResolveWindowsContext(runner, cancellationToken);
        var contextArg = context is not null ? $"--context {context} " : string.Empty;

        var probe = await runner.RunAsync("docker", $"{contextArg}info --format \"{{{{.OSType}}}}\"", cancellationToken: cancellationToken);
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
        return CheckResult.Failure(
            $"Docker Desktop is targeting '{mode}' containers. Switching to Windows containers mode will make " +
            "existing Linux containers and images temporarily inaccessible. Stop any running containers before fixing.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        // Refuse to switch if containers are running — switching modes makes Linux containers
        // inaccessible and forcefully stopping Docker Desktop would kill them without warning.
        var running = await runner.RunAsync("docker", "ps -q", cancellationToken: cancellationToken);
        if (running.IsSuccess)
        {
            var ids = running.StandardOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            if (ids.Count > 0)
                return FixResult.Failure(
                    $"{ids.Count} container(s) are currently running. Stop them before switching to Windows containers mode, " +
                    "as the switch will make all Linux containers and images temporarily inaccessible.");
        }

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

        // Poll for the Windows engine to come up. Re-resolve the context each tick —
        // Docker Desktop may create the desktop-windows context during its restart.
        if (await PollForWindowsMode(runner, cancellationToken))
            return FixResult.Success("Docker Desktop switched to Windows containers.");

        // The desktop-windows context still doesn't exist. This happens when Docker was
        // previously started only in Linux mode — the context is written by Docker Desktop
        // on startup, not by the switch command at runtime.
        // Fix: stop Docker Desktop, run the switch (which now writes to settings), then
        // restart so Docker Desktop initialises fresh with the Windows engine.
        return await SwitchViaRestart(runner, dockerCli, cancellationToken);
    }

    private static async Task<bool> PollForWindowsMode(ProcessRunner runner, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            var context = await ResolveWindowsContext(runner, cancellationToken);
            var contextArg = context is not null ? $"--context {context} " : string.Empty;

            var validation = await runner.RunAsync("docker", $"{contextArg}info --format \"{{{{.OSType}}}}\"", cancellationToken: cancellationToken);
            if (!validation.IsSuccess) continue;

            var osType = ExtractOsType(validation.StandardOutput);
            if (string.Equals(osType, "windows", StringComparison.OrdinalIgnoreCase))
            {
                // Pin the elevated process to the Windows context so downstream docker calls
                // (HnsNatHealth, compose runs, etc.) target the right engine.
                if (context is not null)
                    await runner.RunAsync("docker", $"context use {context}", cancellationToken: cancellationToken);

                // The engine switch restarts com.docker.service internally — wait for it.
                await EnsureHelperServiceRunning(runner, cancellationToken);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Stops Docker Desktop, applies the Windows engine switch to the settings file,
    /// then restarts Docker Desktop so it initialises with the desktop-windows context.
    /// Used when the live switch fails because the context was never created at startup.
    /// </summary>
    private static async Task<FixResult> SwitchViaRestart(ProcessRunner runner, string dockerCli, CancellationToken cancellationToken)
    {
        // Stop Docker Desktop — the switch command writes to the settings file when Docker is not running.
        await runner.RunAsync("powershell",
            "-NoProfile -NonInteractive -Command \"Get-Process 'Docker Desktop' -ErrorAction SilentlyContinue | Stop-Process -Force\"",
            cancellationToken: cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

        // Switch with Docker Desktop stopped — this patches settings rather than sending a runtime command.
        await runner.RunAsync(dockerCli, "-SwitchWindowsEngine", cancellationToken: cancellationToken);

        // Restart Docker Desktop; it will read the updated settings and create the desktop-windows context.
        var desktopExe = ResolveDockerDesktopPath();
        if (desktopExe is not null)
        {
            await runner.RunAsync("powershell",
                $"-NoProfile -NonInteractive -Command \"Start-Process -FilePath '{desktopExe}'\"",
                cancellationToken: cancellationToken);
        }

        // Give Docker Desktop time to initialise its contexts before polling.
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        if (await PollForWindowsMode(runner, cancellationToken))
            return FixResult.Success("Docker Desktop restarted and switched to Windows containers.");

        return FixResult.Failure("Docker Desktop restarted but did not report Windows containers in time. A reboot may be required.");
    }

    private static string? ResolveDockerDesktopPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Docker", "Docker", "Docker Desktop.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Returns the name of the Docker context that targets the Windows containers engine,
    /// or <c>null</c> if no such context exists yet (e.g. first launch before Docker Desktop
    /// has written its context files). Falls back gracefully to the default context in callers.
    /// </summary>
    private static async Task<string?> ResolveWindowsContext(ProcessRunner runner, CancellationToken cancellationToken)
    {
        var list = await runner.RunAsync("docker", "context ls --format \"{{.Name}}\"", cancellationToken: cancellationToken);
        if (!list.IsSuccess)
            return null;

        var contexts = list.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Trim('"'))
            .ToList();

        // Docker Desktop creates "desktop-windows" for Windows containers. Check for it first,
        // then fall back to any context whose name suggests Windows containers.
        return contexts.FirstOrDefault(c => c.Equals("desktop-windows", StringComparison.OrdinalIgnoreCase))
            ?? contexts.FirstOrDefault(c => c.Contains("windows", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task EnsureHelperServiceRunning(ProcessRunner runner, CancellationToken cancellationToken)
    {
        // Poll for up to 30 s; the service usually recovers within one or two ticks.
        for (var i = 0; i < 6; i++)
        {
            var svc = await runner.RunAsync("powershell",
                "-NoProfile -NonInteractive -Command \"(Get-Service -Name 'com.docker.service' -ErrorAction SilentlyContinue).Status\"",
                cancellationToken: cancellationToken);
            if (string.Equals(svc.StandardOutput.Trim(), "Running", StringComparison.OrdinalIgnoreCase))
                return;
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        // Best-effort start; ignore failure — DockerHelperServiceCheck will surface it if needed.
        await runner.RunAsync("net", "start com.docker.service", cancellationToken: cancellationToken);
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
