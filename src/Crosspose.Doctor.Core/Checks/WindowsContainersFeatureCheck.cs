using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

public sealed class WindowsContainersFeatureCheck : ICheckFix
{
    public string Name => "windows-containers-feature";
    public string Description => "Ensures the Windows Containers and Hyper-V optional features are enabled (required for Windows containers mode).";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("powershell",
            "-NoProfile -NonInteractive -Command \"(Get-WindowsOptionalFeature -Online -FeatureName Containers -ErrorAction SilentlyContinue).State\"",
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
            return CheckResult.Failure("Unable to query Windows optional features. Try running as administrator.");

        var state = result.StandardOutput.Trim();

        if (string.Equals(state, "Enabled", StringComparison.OrdinalIgnoreCase))
            return CheckResult.Success("Windows Containers feature is enabled.");

        if (string.Equals(state, "EnablePending", StringComparison.OrdinalIgnoreCase))
            return CheckResult.Failure("Windows Containers feature is pending a system restart.");

        return CheckResult.Failure($"Windows Containers feature is '{state}'. Enable it and restart.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("powershell",
            "-NoProfile -NonInteractive -Command \"Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V,Containers -All -NoRestart\"",
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
            return FixResult.Failure($"Failed to enable Windows Containers feature: {result.StandardError.Trim()}");

        return FixResult.Failure("Windows Containers and Hyper-V features enabled. A system restart is required before Docker can switch to Windows containers mode.");
    }
}
