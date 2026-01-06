using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public sealed class WslCheck : ICheckFix
{
    public string Name => "wsl";
    public string Description => "Verifies Windows Subsystem for Linux is enabled for Linux container support.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("wsl", "--status", cancellationToken: cancellationToken);
        if (result.IsSuccess)
        {
            return CheckResult.Success("WSL is enabled.");
        }

        return CheckResult.Failure(string.IsNullOrWhiteSpace(result.StandardError)
            ? "WSL not available."
            : result.StandardError);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("wsl", "--install", cancellationToken: cancellationToken);
        return result.IsSuccess
            ? FixResult.Success("WSL install invoked. A reboot may be required.")
            : FixResult.Failure(string.IsNullOrWhiteSpace(result.StandardError)
                ? "wsl --install failed. Try running from an elevated PowerShell."
                : result.StandardError);
    }
}
