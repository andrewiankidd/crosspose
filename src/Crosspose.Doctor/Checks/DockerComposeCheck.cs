using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public sealed class DockerComposeCheck : ICheckFix
{
    public string Name => "docker-compose";
    public string Description => "Ensures Docker Desktop/Compose is installed to run generated workloads.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var dockerCompose = await runner.RunAsync("docker", "compose version", cancellationToken: cancellationToken);
        if (dockerCompose.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(dockerCompose.StandardOutput)
                ? "docker compose available"
                : dockerCompose.StandardOutput.Split(Environment.NewLine).First();
            return CheckResult.Success(message);
        }

        var legacy = await runner.RunAsync("docker-compose", "--version", cancellationToken: cancellationToken);
        if (legacy.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(legacy.StandardOutput)
                ? "docker-compose available"
                : legacy.StandardOutput.Split(Environment.NewLine).First();
            return CheckResult.Success(message);
        }

        var error = !string.IsNullOrWhiteSpace(dockerCompose.StandardError)
            ? dockerCompose.StandardError
            : "docker compose / docker-compose not available.";
        return CheckResult.Failure(error);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        if (!await WingetAvailable(runner, cancellationToken))
        {
            return FixResult.Failure("winget not available; install Docker Desktop manually from https://www.docker.com/products/docker-desktop/");
        }

        var result = await runner.RunAsync("winget", "install -e --id Docker.DockerDesktop -h", cancellationToken: cancellationToken);
        return result.IsSuccess
            ? FixResult.Success("Docker Desktop installation attempted via winget.")
            : FixResult.Failure($"winget install Docker.DockerDesktop failed: {result.StandardError}");
    }

    private static async Task<bool> WingetAvailable(ProcessRunner runner, CancellationToken token)
    {
        var result = await runner.RunAsync("winget", "--version", cancellationToken: token);
        return result.IsSuccess;
    }
}
