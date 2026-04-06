using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Detects corruption on the Windows HNS nat network that prevents Docker from attaching
/// Windows containers. Manifests as HNS error 0x32 when running docker compose up.
///
/// Detection: probes endpoint creation on the nat network. Falls back to the built-in
/// Windows HostNetworkingService module if Docker Desktop's HNS.psm1 is not present.
///
/// Fix strategy — IMPORTANT: do NOT restart the HNS service.
/// On machines with VFP-based security software (Forescout FSE, CrowdStrike, etc.),
/// restarting HNS deletes the nat network and Docker cannot recreate it while the VFP
/// policy is active. The only recovery from that state is a machine reboot (Docker
/// creates the nat network during boot before VFP policies are fully loaded).
///
/// Safe fix: restart only com.docker.service (the Windows Docker engine daemon).
/// This re-registers Docker against the existing HNS nat network without destroying it.
/// If the nat network is missing entirely, instruct the user to reboot.
/// </summary>
public sealed class HnsNatHealthCheck : ICheckFix
{
    // Script: probe nat network using Docker's HNS module or the built-in Windows module.
    // Outputs: healthy | hns-stopped | no-nat | no-module | error: <message>
    private const string ProbeScript =
        "$ErrorActionPreference='Stop';" +
        "try {" +
        "  $svc=Get-Service -Name hns -ErrorAction Stop;" +
        "  if ($svc.Status -ne 'Running'){Write-Output 'hns-stopped';exit}" +
        // Try Docker's bundled module first, fall back to built-in Windows module
        "  $mod=Join-Path $env:ProgramFiles 'Docker\\Docker\\resources\\HNS.psm1';" +
        "  if (Test-Path $mod){Import-Module $mod -Force -ErrorAction SilentlyContinue}" +
        "  else{Import-Module HostNetworkingService -Force -ErrorAction SilentlyContinue}" +
        "  $nat=Get-HnsNetwork -ErrorAction Stop|Where-Object{$_.Name -eq 'nat' -and $_.Type -eq 'nat'};" +
        "  if (!$nat){Write-Output 'no-nat';exit}" +
        // Only probe endpoint creation if Docker's module is available (has New-HnsEndpoint)
        "  if (Test-Path $mod){" +
        "    $ep=New-HnsEndpoint -NetworkId $nat.Id -Name 'crosspose-hns-probe' -ErrorAction Stop;" +
        "    if ($ep){Remove-HnsEndpoint -Id $ep.Id -ErrorAction SilentlyContinue;Write-Output 'healthy'}" +
        "    else{Write-Output 'endpoint-null'}" +
        "  } else {Write-Output 'healthy'}" +
        "} catch {Write-Output \"error: $($_.Exception.Message)\"}";

    public string Name => "hns-nat-health";
    public string Description => "Verifies the Windows HNS nat network exists and Docker can attach containers to it. HNS issues cause error 0x32 when starting Windows containers.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await ProbeHnsAsync(runner, cancellationToken);

        return result switch
        {
            "healthy" => CheckResult.Success("HNS nat network is healthy — Docker can attach Windows containers."),
            "hns-stopped" => CheckResult.Failure("HNS service is not running. Windows containers cannot start."),
            "no-nat" => CheckResult.Failure("Docker nat network not found in HNS. A machine reboot is required to recreate it."),
            "endpoint-null" => CheckResult.Failure("HNS endpoint creation returned null — nat network is corrupt. Restart Docker Desktop or reboot."),
            _ when result.StartsWith("error:", StringComparison.OrdinalIgnoreCase) =>
                CheckResult.Failure($"HNS probe failed: {result["error:".Length..].Trim()}. Windows containers cannot start."),
            _ => CheckResult.Failure($"Unexpected HNS probe result: {result}")
        };
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var probe = await ProbeHnsAsync(runner, cancellationToken);

        // If HNS is stopped, start it — but do NOT restart it if it is already running.
        // Restarting HNS deletes the nat network. On machines with VFP-based security
        // software (Forescout FSE, CrowdStrike etc.) Docker cannot recreate the nat
        // network while VFP policies are active; only a machine reboot can recover.
        if (probe == "hns-stopped")
        {
            logger.LogInformation("Starting HNS service...");
            var start = await runner.RunAsync("net", "start hns", cancellationToken: cancellationToken);
            if (!start.IsSuccess)
            {
                var startError = string.IsNullOrWhiteSpace(start.StandardError) ? start.StandardOutput : start.StandardError;
                if (!startError.Contains("already been started", StringComparison.OrdinalIgnoreCase))
                {
                    return FixResult.Failure($"Failed to start HNS: {startError.Trim()}. Run Doctor as Administrator.");
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        if (probe == "no-nat")
        {
            // nat network is gone — only a reboot can safely recreate it.
            return FixResult.Failure(
                "The HNS nat network does not exist. Please reboot your machine — Docker will recreate it " +
                "during startup before security software loads its network policies.");
        }

        // For endpoint-null and error states: restart the Windows Docker engine daemon.
        // This re-registers Docker against the existing HNS nat network without destroying it.
        logger.LogInformation("Restarting Windows Docker engine (com.docker.service)...");
        var restart = await runner.RunAsync(
            "powershell",
            "-NoProfile -Command \"Restart-Service 'com.docker.service' -Force\"",
            cancellationToken: cancellationToken);

        if (!restart.IsSuccess)
        {
            var err = string.IsNullOrWhiteSpace(restart.StandardError) ? restart.StandardOutput : restart.StandardError;
            return FixResult.Failure($"Failed to restart Docker service: {err.Trim()}. Run Doctor as Administrator.");
        }

        // Wait for Docker Windows engine to come back up
        logger.LogInformation("Waiting for Docker Windows engine to become ready...");
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var dockerInfo = await runner.RunAsync(
                "docker",
                "-H npipe:////./pipe/dockerDesktopWindowsEngine info --format \"{{.ServerVersion}}\"",
                cancellationToken: cancellationToken);
            if (dockerInfo.IsSuccess && !string.IsNullOrWhiteSpace(dockerInfo.StandardOutput))
            {
                logger.LogInformation("Docker Windows engine ready (v{Version})", dockerInfo.StandardOutput.Trim());
                break;
            }
        }

        var postFix = await ProbeHnsAsync(runner, cancellationToken);
        if (postFix is "healthy" or "no-module")
        {
            return FixResult.Success("Docker Windows engine restarted. HNS nat network is now healthy.");
        }

        if (postFix == "no-nat")
        {
            return FixResult.Failure(
                "The HNS nat network is still missing after Docker restart. Please reboot your machine.");
        }

        return FixResult.Failure(
            $"HNS probe still unhealthy after restart ({postFix}). " +
            "Try rebooting, or restart Docker Desktop manually from the system tray.");
    }

    private static async Task<string> ProbeHnsAsync(ProcessRunner runner, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            "powershell",
            $"-NoProfile -NonInteractive -Command \"{ProbeScript}\"",
            cancellationToken: cancellationToken);

        var output = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(output) && !result.IsSuccess)
        {
            var err = result.StandardError.Trim();
            return $"error: {(string.IsNullOrWhiteSpace(err) ? "powershell probe failed" : err)}";
        }

        // Take only the last non-empty line (PowerShell may emit module load output before result)
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[^1].Trim() : "error: no output";
    }
}
