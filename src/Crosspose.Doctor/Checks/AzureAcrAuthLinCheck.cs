using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using System;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public sealed class AzureAcrAuthLinCheck : ICheckFix
{
    private readonly string _registryName;
    private const string PodmanUsername = "00000000-0000-0000-0000-000000000000";
    private const string RootAuthDirectory = "/root/.config/containers";
    private const string RootAuthFile = $"{RootAuthDirectory}/auth.json";

    public AzureAcrAuthLinCheck(string registryName)
    {
        _registryName = registryName;
    }

    public string Name => $"azure-acr-auth-lin:{_registryName}";
    public string Description => $"Optional: verifies Podman authentication is available for {_registryName}.";
    public bool IsAdditional => true;
    public string AdditionalKey => $"azure-acr-auth-lin:{_registryName}";
    public bool CanFix => true;

        public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
        {
            var distro = CrossposeEnvironment.WslDistro;
            var server = GetLoginServer();
            var result = await RunWslAsync(
                runner,
                cancellationToken,
                "-d",
                distro,
                "-u",
                "root",
                "--",
                "podman",
                "login",
                "--authfile",
                RootAuthFile,
                "--get-login",
                server);
            if (!result.IsSuccess)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"Podman login not found for {server}."
                    : result.StandardError.Trim();
                return CheckResult.Failure(error);
            }

            var tokenResult = await RunWslAsync(
                runner,
                cancellationToken,
                "-d",
                distro,
                "-u",
                "root",
                "--",
                "cat",
                RootAuthFile);
            if (!tokenResult.IsSuccess)
            {
                var error = string.IsNullOrWhiteSpace(tokenResult.StandardError)
                    ? $"Unable to read {RootAuthFile} in WSL."
                    : tokenResult.StandardError.Trim();
                return CheckResult.Failure(error);
            }

            var token = TryExtractTokenFromAuthJson(tokenResult.StandardOutput, server);
            if (token is null)
            {
                return CheckResult.Failure($"Failed to parse auth token for {server} in Podman config.");
            }

            if (AuthTokenInspector.TryGetExpiration(token, out var expiration))
            {
                if (AuthTokenInspector.IsExpired(expiration))
                {
                    return CheckResult.Failure($"Podman ACR token for {server} expired at {expiration:u}.");
                }

                return CheckResult.Success($"Podman login found for {server} (expires {expiration:u}).");
            }

            return CheckResult.Success($"Already logged into {server} (Podman).");
        }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var winCheck = new AzureAcrAuthWinCheck(_registryName);
        var token = await winCheck.GetAccessTokenAsync(runner, logger, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return FixResult.Failure("Unable to retrieve Azure ACR access token via az.");
        }

        var distro = CrossposeEnvironment.WslDistro;
        var server = GetLoginServer();
        var ensureDirResult = await RunWslAsync(
            runner,
            cancellationToken,
            "-d",
            distro,
            "-u",
            "root",
            "--",
            "mkdir",
            "-p",
            RootAuthDirectory);
        if (!ensureDirResult.IsSuccess)
        {
            var dirError = string.IsNullOrWhiteSpace(ensureDirResult.StandardError)
                ? ensureDirResult.StandardOutput
                : ensureDirResult.StandardError;
            return FixResult.Failure(string.IsNullOrWhiteSpace(dirError)
                ? $"Failed to create {RootAuthDirectory} in WSL."
                : dirError.Trim());
        }

        var loginResult = await RunWslAsync(
            runner,
            cancellationToken,
            "-d",
            distro,
            "-u",
            "root",
            "--",
            "podman",
            "login",
            "--authfile",
            RootAuthFile,
            server,
            "--username",
            PodmanUsername,
            "--password",
            token);

        if (!loginResult.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(loginResult.StandardError)
                ? loginResult.StandardOutput
                : loginResult.StandardError;
            return FixResult.Failure(string.IsNullOrWhiteSpace(error)
                ? $"Failed to run podman login for {server}."
                : error.Trim());
        }

        return FixResult.Success("Podman login refreshed for Azure ACR.");
    }

    private string GetLoginServer()
    {
        if (string.IsNullOrWhiteSpace(_registryName)) return _registryName;
        return _registryName.Contains('.', StringComparison.Ordinal)
            ? _registryName
            : $"{_registryName}.azurecr.io";
    }

        private static string? TryExtractTokenFromAuthJson(string json, string server)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(server)) return null;
            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("auths", out var auths)) return null;
                if (!auths.TryGetProperty(server, out var entry)) return null;
                if (!entry.TryGetProperty("auth", out var authValueElement)) return null;
                var authValue = authValueElement.GetString();
                if (string.IsNullOrWhiteSpace(authValue)) return null;
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(authValue)));
                var split = decoded.Split(':', 2);
                if (split.Length < 2) return null;
                return split[1];
            }
            catch (JsonException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static string PadBase64(string value)
        {
            var padded = value.Trim();
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
                default: break;
            }
            return padded;
        }

        private static Task<ProcessResult> RunWslAsync(ProcessRunner runner, CancellationToken cancellationToken, params string[] args)
        {
            var wsl = new WslRunner(runner);
            return wsl.ExecAsync(args, cancellationToken: cancellationToken);
        }
}
