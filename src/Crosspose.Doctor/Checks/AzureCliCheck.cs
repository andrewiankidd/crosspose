using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public sealed class AzureCliCheck : ICheckFix
{
    public string Name => "azure-cli";
    public string Description => "Optional: verifies Azure CLI (az) is installed for Azure-based workflows.";
    public bool IsAdditional => true;
    public string AdditionalKey => "azure-cli";
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var azCmd = await GetAzCommandAsync(runner, cancellationToken);
        var result = await runner.RunAsync(azCmd, "--version", cancellationToken: cancellationToken);
        if (result.IsSuccess || result.StandardOutput.Contains("azure-cli", StringComparison.OrdinalIgnoreCase))
        {
            var firstLine = result.StandardOutput.Split(Environment.NewLine).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
            return CheckResult.Success(firstLine ?? "az available.");
        }

        // Fallback to locating az to avoid false negatives when exit code is non-zero but executable exists
        var locate = await runner.RunAsync("where", "az", cancellationToken: cancellationToken);
        if (locate.IsSuccess || locate.StandardOutput.Contains("az", StringComparison.OrdinalIgnoreCase))
        {
            var pathLine = locate.StandardOutput.Split(Environment.NewLine).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
            return CheckResult.Success($"az available at {pathLine}");
        }

        var error = string.IsNullOrWhiteSpace(result.StandardError)
            ? "az not available."
            : result.StandardError;
        return CheckResult.Failure(error);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var winget = await runner.RunAsync("winget", "--version", cancellationToken: cancellationToken);
        if (!winget.IsSuccess)
        {
            return FixResult.Failure("winget not available; install Azure CLI manually from https://learn.microsoft.com/cli/azure/install-azure-cli");
        }

        var install = await runner.RunAsync("winget", "install -e --id Microsoft.AzureCLI --accept-package-agreements --accept-source-agreements -h", cancellationToken: cancellationToken);
        return install.IsSuccess
            ? FixResult.Success("Azure CLI installation attempted via winget.")
            : FixResult.Failure($"winget install Microsoft.AzureCLI failed: {install.StandardError}");
    }

    private static async Task<string> GetAzCommandAsync(ProcessRunner runner, CancellationToken token)
    {
        // Prefer az.cmd on Windows to avoid launching the wrong binary (common when multiple az shims exist).
        var whereCmd = await runner.RunAsync("where", "az.cmd", cancellationToken: token);
        if (whereCmd.IsSuccess || whereCmd.StandardOutput.Contains("az.cmd", StringComparison.OrdinalIgnoreCase))
        {
            var path = whereCmd.StandardOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => l.IndexOf("az.cmd", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrWhiteSpace(path)) return path.Trim();
            return "az.cmd";
        }

        var where = await runner.RunAsync("where", "az", cancellationToken: token);
        if (where.IsSuccess || where.StandardOutput.Contains("az", StringComparison.OrdinalIgnoreCase))
        {
            var path = where.StandardOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => l.IndexOf("az", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrWhiteSpace(path)) return path.Trim();
            return "az";
        }

        return "az.cmd";
    }
}
