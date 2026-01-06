using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Validates that podman is installed within the dedicated crosspose WSL distro.
/// </summary>
public sealed class PodmanWslCheck : ICheckFix
{
    public string Name => "podman-wsl";
    public string Description => "Ensures Podman is available inside the crosspose WSL distro.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var distro = CrossposeEnvironment.WslDistro;
        var result = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "podman", "--version");
        if (result.IsSuccess)
        {
            var firstLine = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? "podman is available inside WSL."
                : result.StandardOutput.Trim().Split(Environment.NewLine)[0];
            return CheckResult.Success(firstLine);
        }

        var error = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"podman not found inside '{distro}'."
            : result.StandardError.Trim();
        return CheckResult.Failure(error);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var distro = CrossposeEnvironment.WslDistro;
        var installCommand = "apk update && apk add podman";
        var result = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "sh", "-c", $"\"{installCommand}\"");
        if (!result.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            return FixResult.Failure($"Failed to install podman inside '{distro}'. {error}");
        }

        return FixResult.Success("Installed podman inside the crosspose WSL distro.");
    }

    private static Task<ProcessResult> RunWslAsync(ProcessRunner runner, CancellationToken cancellationToken, params string[] args)
    {
        var wsl = new WslRunner(runner);
        return wsl.ExecAsync(args, cancellationToken: cancellationToken);
    }
}
