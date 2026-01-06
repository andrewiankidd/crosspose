using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Ensures sudo is installed inside the crosspose WSL distro (needed for podman compose).
/// </summary>
public sealed class SudoCheck : ICheckFix
{
    public string Name => "sudo-check";
    public string Description => "Ensures sudo is available inside the crosspose WSL distro.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var distro = CrossposeEnvironment.WslDistro;
        var result = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "which", "sudo");
        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return CheckResult.Success("sudo is available.");
        }

        return CheckResult.Failure("sudo is not installed inside the crosspose WSL distro.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var distro = CrossposeEnvironment.WslDistro;
        var installCommand = "apk update && apk add sudo";
        var result = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "sh", "-c", $"\"{installCommand}\"");
        if (!result.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            error = string.IsNullOrWhiteSpace(error) ? "Failed to install sudo." : error;
            return FixResult.Failure(error.Trim());
        }

        return FixResult.Success("Installed sudo inside the crosspose WSL distro.");
    }

    private static Task<ProcessResult> RunWslAsync(ProcessRunner runner, CancellationToken cancellationToken, params string[] args)
    {
        var wsl = new WslRunner(runner);
        return wsl.ExecAsync(args, cancellationToken: cancellationToken);
    }
}
