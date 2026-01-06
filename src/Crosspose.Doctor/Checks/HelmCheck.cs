using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public sealed class HelmCheck : ICheckFix
{
    public string Name => "helm";
    public string Description => "Required to fetch and render Helm charts before dekompose.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("helm", "version --short", cancellationToken: cancellationToken);
        if (result.IsSuccess)
        {
            var versionLine = result.StandardOutput.Split(Environment.NewLine).FirstOrDefault()?.Trim();
            return CheckResult.Success(versionLine ?? "helm available.");
        }

        return CheckResult.Failure(string.IsNullOrWhiteSpace(result.StandardError)
            ? "helm not available."
            : result.StandardError);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        if (!await WingetAvailable(runner, cancellationToken))
        {
            return FixResult.Failure("winget not available; install Helm manually from https://helm.sh/docs/intro/install/");
        }

        var result = await runner.RunAsync(
            "winget",
            "install -e --id Helm.Helm -h --source winget --accept-source-agreements --accept-package-agreements",
            cancellationToken: cancellationToken);
        return result.IsSuccess
            ? FixResult.Success("Helm installation attempted via winget.")
            : FixResult.Failure($"winget install Helm.Helm failed: {result.StandardError}");
    }

    private static async Task<bool> WingetAvailable(ProcessRunner runner, CancellationToken token)
    {
        var result = await runner.RunAsync("winget", "--version", cancellationToken: token);
        return result.IsSuccess;
    }
}
