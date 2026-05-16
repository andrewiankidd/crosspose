using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

public sealed class DockerHelperServiceCheck : ICheckFix
{
    public string Name => "docker-helper-service";
    public string Description => "Ensures the Docker Desktop privileged helper service (com.docker.service) is running.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;
    public bool AutoFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("powershell",
            "-NoProfile -NonInteractive -Command \"(Get-Service -Name 'com.docker.service' -ErrorAction SilentlyContinue).Status\"",
            cancellationToken: cancellationToken);

        var status = result.StandardOutput.Trim();

        if (string.IsNullOrWhiteSpace(status))
            return CheckResult.Failure("Docker Desktop helper service not found. Is Docker Desktop installed?");

        if (string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase))
            return CheckResult.Success("Docker Desktop helper service is running.");

        return CheckResult.Failure($"Docker Desktop helper service is '{status}'.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        // Ensure the service starts automatically on future reboots.
        await runner.RunAsync("sc", "config com.docker.service start=auto", cancellationToken: cancellationToken);

        var result = await runner.RunAsync("net", "start com.docker.service", cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            var output = (result.StandardOutput + result.StandardError).ToLowerInvariant();
            if (output.Contains("already been started") || output.Contains("already running"))
                return FixResult.Success("Docker Desktop helper service is already running.");

            return FixResult.Failure($"Failed to start Docker Desktop helper service: {result.StandardError.Trim()}");
        }

        return FixResult.Success("Docker Desktop helper service started and set to automatic startup.");
    }
}
