using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Crosspose.Doctor.Checks;

public sealed class AzureAcrAuthWinCheck : ICheckFix
{
    private readonly string _registryName;
    private static readonly HttpClient Http = new();

    public AzureAcrAuthWinCheck(string registryName)
    {
        _registryName = registryName;
    }

    public string Name => $"azure-acr-auth-win:{_registryName}";
    public string Description => $"Optional: verifies Azure ACR auth is available for {_registryName}.";
    public bool IsAdditional => true;
    public string AdditionalKey => $"azure-acr-auth-win:{_registryName}";
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var azCmd = await GetAzCommandAsync(runner, cancellationToken);
        var result = await runner.RunAsync(azCmd, $"acr login --name {_registryName} --expose-token --query accessToken -o json", cancellationToken: cancellationToken);
        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var token = result.StandardOutput.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(token))
            {
                if (AuthTokenInspector.TryGetExpiration(token, out var expiration))
                {
                    if (AuthTokenInspector.IsExpired(expiration))
                    {
                        return CheckResult.Failure($"ACR token for {_registryName} expired at {expiration:u}.");
                    }
                    var catalogResult = await CheckCatalogAccessAsync(token, logger, cancellationToken);
                    if (!catalogResult.IsSuccessful)
                    {
                        return catalogResult;
                    }
                    return CheckResult.Success($"ACR token retrieved (expires {expiration:u}). Catalog access OK.");
                }

                var catalogFallback = await CheckCatalogAccessAsync(token, logger, cancellationToken);
                if (!catalogFallback.IsSuccessful)
                {
                    return catalogFallback;
                }
                return CheckResult.Success("ACR token retrieved. Catalog access OK.");
            }
        }

        var error = string.IsNullOrWhiteSpace(result.StandardError)
            ? "No valid ACR token found."
            : result.StandardError;
        return CheckResult.Failure(error);
    }

    public async Task<string?> GetAccessTokenAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var azCmd = await GetAzCommandAsync(runner, cancellationToken);
        var tokenResult = await runner.RunAsync(azCmd, $"acr login --name {_registryName} --expose-token --query accessToken -o tsv", cancellationToken: cancellationToken);
        if (tokenResult.IsSuccess && !string.IsNullOrWhiteSpace(tokenResult.StandardOutput))
        {
            return tokenResult.StandardOutput.Trim().Trim('"');
        }
        logger.LogWarning("Failed to retrieve ACR token for {Registry}: {Error}", _registryName, tokenResult.StandardError);
        return null;
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var azCmd = await GetAzCommandAsync(runner, cancellationToken);
        var login = await runner.RunAsync(azCmd, "login", cancellationToken: cancellationToken);
        if (!login.IsSuccess)
        {
            var err = string.IsNullOrWhiteSpace(login.StandardError) ? "az login failed." : login.StandardError;
            return FixResult.Failure(err);
        }

        var acrLogin = await runner.RunAsync(azCmd, $"acr login --name {_registryName}", cancellationToken: cancellationToken);
        if (!acrLogin.IsSuccess)
        {
            var err = string.IsNullOrWhiteSpace(acrLogin.StandardError) ? "az acr login failed." : acrLogin.StandardError;
            return FixResult.Failure(err);
        }

        var verify = await RunAsync(runner, logger, cancellationToken);
        if (!verify.IsSuccessful)
        {
            return FixResult.Failure(verify.Message);
        }

        return FixResult.Success("Azure ACR authentication refreshed.");
    }

    private static async Task<string> GetAzCommandAsync(ProcessRunner runner, CancellationToken token)
    {
        // Prefer az.cmd on Windows to avoid wrong binary launches.
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

    private async Task<CheckResult> CheckCatalogAccessAsync(string bearerToken, ILogger logger, CancellationToken cancellationToken)
    {
        var url = $"https://{_registryName}.azurecr.io/v2/_catalog?n=1";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            var resp = await Http.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                return CheckResult.Success("ACR catalog access available.");
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            {
                return CheckResult.Failure($"ACR catalog access denied (HTTP {(int)resp.StatusCode}). Ensure your identity has AcrReader/AcrPull for {_registryName}.");
            }

            return CheckResult.Failure($"ACR catalog access failed with HTTP {(int)resp.StatusCode}.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ACR catalog probe failed for {Registry}", _registryName);
            return CheckResult.Failure("ACR catalog access probe failed.");
        }
    }
}
