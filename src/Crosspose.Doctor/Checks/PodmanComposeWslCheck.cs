using System;
using System.Text;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Ensures the podman compose plugin is available inside the crosspose WSL distro.
/// </summary>
public sealed class PodmanComposeWslCheck : ICheckFix
{
    public string Name => "podman-compose-wsl";
    public string Description => "Ensures Podman compose can run inside the crosspose WSL distro.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var distro = CrossposeEnvironment.WslDistro;
        var composeResult = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "podman", "compose", "version");
        if (!composeResult.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(composeResult.StandardError)
                ? composeResult.StandardOutput
                : composeResult.StandardError;
            if (string.IsNullOrWhiteSpace(error))
            {
                error = "podman compose is not available inside the crosspose WSL distro.";
            }
            return CheckResult.Failure(error.Trim());
        }

        var iptablesResult = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "sh", "-c", "command -v iptables && command -v ip6tables");
        if (!iptablesResult.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(iptablesResult.StandardError)
                ? iptablesResult.StandardOutput
                : iptablesResult.StandardError;
            if (string.IsNullOrWhiteSpace(error))
            {
                error = "iptables binaries are missing inside the crosspose WSL distro.";
            }
            return CheckResult.Failure(error.Trim());
        }

        var info = string.IsNullOrWhiteSpace(composeResult.StandardOutput)
            ? "podman compose and iptables are available."
            : composeResult.StandardOutput.Trim().Split(Environment.NewLine)[0];
        return CheckResult.Success(info);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var distro = CrossposeEnvironment.WslDistro;
        var wslUser = CrossposeEnvironment.WslUser;
        var installCommand = "apk update && apk add podman podman-compose iptables ip6tables";
        var result = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "sh", "-c", $"\"{installCommand}\"");
        if (!result.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            error = string.IsNullOrWhiteSpace(error) ? "Unknown failure installing podman-compose." : error;
            return FixResult.Failure(error.Trim());
        }

        var configToml = BuildPodmanConfigToml();
        var configBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configToml));

        var rootConfigure = string.Join(" && ", new[]
        {
            "mkdir -p /etc/containers/containers.conf.d",
            $"printf '{configBase64}' | base64 -d > /etc/containers/containers.conf.d/100-crosspose-compose.conf"
        });

        var rootResult = await RunWslAsync(runner, cancellationToken, "-d", distro, "-u", "root", "--", "sh", "-c", $"\"{rootConfigure}\"");
        if (!rootResult.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(rootResult.StandardError) ? rootResult.StandardOutput : rootResult.StandardError;
            error = string.IsNullOrWhiteSpace(error) ? "Failed to configure root Podman settings." : error;
            return FixResult.Failure(error.Trim());
        }

        var userConfigure = string.Join(" && ", new[]
        {
            $"mkdir -p /home/{wslUser}/.config/containers",
            $"printf '{configBase64}' | base64 -d > /home/{wslUser}/.config/containers/containers.conf"
        });

        var userConfigureResult = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "sh", "-c", $"\"{userConfigure}\"");
        if (!userConfigureResult.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(userConfigureResult.StandardError) ? userConfigureResult.StandardOutput : userConfigureResult.StandardError;
            error = string.IsNullOrWhiteSpace(error) ? "Failed to configure Podman compose provider." : error;
            return FixResult.Failure(error.Trim());
        }

        return FixResult.Success("Installed podman-compose and configured it as the compose provider inside the crosspose WSL distro.");
    }

    private static Task<ProcessResult> RunWslAsync(ProcessRunner runner, CancellationToken cancellationToken, params string[] args)
    {
        var wsl = new WslRunner(runner);
        return wsl.ExecAsync(args, cancellationToken: cancellationToken);
    }

    private static string BuildPodmanConfigToml()
    {
        var engine = new TomlTable
        {
            ["compose_providers"] = new TomlArray { "podman-compose" }
        };

        var document = new TomlTable
        {
            ["engine"] = engine
        };

        return Toml.FromModel(document);
    }
}
