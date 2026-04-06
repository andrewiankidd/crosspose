using Crosspose.Core.Diagnostics;
using Crosspose.Core.Networking;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Detects if the Windows Hyper-V firewall blocks inbound TCP from WSL to the Windows host.
/// This prevents Linux (Podman) containers from reaching Docker Windows containers via
/// the WSL host interface. The fix adds an allow rule for the WSL VM creator ID.
/// </summary>
public sealed class WslToWindowsFirewallCheck : ICheckFix
{
    public string Name => "wsl-to-windows-firewall";
    public string Description => "Ensures WSL can reach Windows host ports (required for Linux→Windows container communication).";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 300;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var wslHostIp = WslHostResolver.GetWslAdapterAddress();
        if (string.IsNullOrWhiteSpace(wslHostIp))
            return CheckResult.Success("No WSL network adapter found — check not applicable.");

        // Try to reach a known Windows service from WSL.
        // Use a TCP connect to the WSL host IP on a port we know netsh portproxy is listening on,
        // or fall back to checking if any portproxy rule exists on the WSL interface.
        var proxyResult = await runner.RunAsync("netsh", "interface portproxy show v4tov4", cancellationToken: cancellationToken);
        var hasWslProxy = proxyResult.IsSuccess &&
                          proxyResult.StandardOutput.Contains(wslHostIp, StringComparison.OrdinalIgnoreCase);

        if (!hasWslProxy)
            return CheckResult.Success("No reverse port proxies on WSL interface — firewall check not applicable.");

        // Check if the Hyper-V firewall rule exists
        var ruleCheck = await runner.RunAsync("powershell",
            "-NoProfile -Command \"Get-NetFirewallHyperVRule -Name 'CrossposeWSLAllow' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Action\"",
            cancellationToken: cancellationToken);

        if (ruleCheck.IsSuccess && ruleCheck.StandardOutput.Trim().Equals("Allow", StringComparison.OrdinalIgnoreCase))
            return CheckResult.Success("Hyper-V firewall allows WSL inbound traffic.");

        // Also check the regular Windows firewall for the WSL interface
        var fwCheck = await runner.RunAsync("netsh",
            $"advfirewall firewall show rule name=all dir=in | findstr /i \"{wslHostIp}\"",
            cancellationToken: cancellationToken);

        if (fwCheck.IsSuccess && !string.IsNullOrWhiteSpace(fwCheck.StandardOutput))
            return CheckResult.Success("Windows firewall allows inbound on WSL interface.");

        return CheckResult.Failure(
            "WSL→Windows inbound may be blocked by Hyper-V or Windows firewall. " +
            "Linux containers cannot reach Windows containers without a firewall allow rule.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var wslHostIp = WslHostResolver.GetWslAdapterAddress();
        if (string.IsNullOrWhiteSpace(wslHostIp))
            return FixResult.Success("No WSL interface — nothing to fix.");

        // Create Hyper-V firewall rule via a temp script (avoids quote-escaping issues in nested Start-Process)
        var scriptPath = Path.Combine(Path.GetTempPath(), "crosspose-hv-fw.ps1");
        var script =
            "try {\n" +
            "  $existing = Get-NetFirewallHyperVRule -Name 'CrossposeWSLAllow' -ErrorAction SilentlyContinue\n" +
            "  if (-not $existing) {\n" +
            "    New-NetFirewallHyperVRule -Name CrossposeWSLAllow " +
            "-DisplayName 'Crosspose: Allow WSL inbound' " +
            "-Direction Inbound " +
            "-VMCreatorId '{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}' " +
            "-Action Allow -Protocol TCP\n" +
            "  }\n" +
            "} catch { }\n";
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        var hvResult = await runner.RunAsync("powershell",
            $"-NoProfile -Command \"Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile','-File','{scriptPath}'\"",
            cancellationToken: cancellationToken);

        // Also add a regular Windows firewall rule for the WSL interface
        var fwResult = await runner.RunElevatedAsync("netsh",
            $"advfirewall firewall add rule name=\"CrossposeWSLInbound\" dir=in action=allow protocol=TCP localip={wslHostIp}",
            cancellationToken);

        try { File.Delete(scriptPath); } catch { }

        // Verify
        var verify = await RunAsync(runner, logger, cancellationToken);
        if (verify.IsSuccessful)
            return FixResult.Success("Added firewall rules for WSL→Windows inbound traffic.");

        return FixResult.Failure(
            "Could not add firewall rules. You may need to manually allow inbound TCP from the WSL subnet. " +
            $"WSL interface IP: {wslHostIp}");
    }
}
