using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

public sealed class WslCheck : ICheckFix
{
    public string Name => "wsl";
    public string Description => "Verifies Windows Subsystem for Linux is enabled for Linux container support.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var result = await runner.RunAsync("wsl", "--status", cancellationToken: cts.Token);
            if (result.IsSuccess)
                return CheckResult.Success("WSL is enabled.");

            return CheckResult.Failure(string.IsNullOrWhiteSpace(result.StandardError)
                ? "WSL not available."
                : result.StandardError);
        }
        catch (OperationCanceledException)
        {
            return CheckResult.Failure("WSL is not responding (timed out).");
        }
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        // Step 1: Try wsl --shutdown (clean path, works when WSL is slow but not fully frozen)
        logger.LogInformation("Attempting wsl --shutdown...");
        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        shutdownCts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var shutdownResult = await runner.RunAsync("wsl", "--shutdown", cancellationToken: shutdownCts.Token);
            if (shutdownResult.IsSuccess)
            {
                using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                statusCts.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    var statusResult = await runner.RunAsync("wsl", "--status", cancellationToken: statusCts.Token);
                    if (statusResult.IsSuccess)
                        return FixResult.Success("WSL restarted successfully.");
                }
                catch (OperationCanceledException) { }
                return FixResult.Success("WSL shutdown completed. It will restart on next use.");
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("wsl --shutdown timed out — WSL is frozen, falling back to service stop.");
        }

        // Step 2: wsl --shutdown hung — stop the WSL service directly (Win11: WslService, older: LxssManager)
        foreach (var service in new[] { "WslService", "LxssManager" })
        {
            logger.LogInformation("Stopping WSL service: {Service}", service);
            using var svcCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            svcCts.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var stopResult = await runner.RunAsync("net", $"stop {service} /y", cancellationToken: svcCts.Token);
                if (stopResult.IsSuccess)
                    return FixResult.Success($"WSL service ({service}) stopped. WSL will restart on next use.");
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("net stop {Service} timed out.", service);
            }
        }

        // Step 3: Force kill wsl processes via taskkill
        logger.LogInformation("Force killing WSL processes...");
        foreach (var proc in new[] { "wslservice.exe", "wsl.exe" })
        {
            using var killCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            killCts.CancelAfter(TimeSpan.FromSeconds(10));
            try { await runner.RunAsync("taskkill", $"/F /IM {proc} /T", cancellationToken: killCts.Token); }
            catch (OperationCanceledException) { }
        }

        // Verify after kill
        using var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        verifyCts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var verify = await runner.RunAsync("wsl", "--status", cancellationToken: verifyCts.Token);
            if (verify.IsSuccess)
                return FixResult.Success("WSL processes killed and service recovered.");
        }
        catch (OperationCanceledException) { }

        // Step 4: WSL not installed at all
        var installResult = await runner.RunAsync("wsl", "--install", cancellationToken: cancellationToken);
        return installResult.IsSuccess
            ? FixResult.Success("WSL install invoked. A reboot may be required.")
            : FixResult.Failure("Could not restart WSL. A system reboot may be required.");
    }
}
