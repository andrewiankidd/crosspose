using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public sealed class AzureAcrAuthWinCheck : ICheckFix
{
    private readonly string _registryName;

    public AzureAcrAuthWinCheck(string registryName)
    {
        _registryName = registryName;
    }

    public string Name => $"azure-acr-auth-win:{_registryName}";
    public string Description => $"Verifies Azure ACR auth (Windows/Docker) is available for {_registryName}.";
    public bool IsAdditional => true;
    public string AdditionalKey => $"azure-acr-auth-win:{_registryName}";
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 1800; // re-check every 30 min; ACR tokens expire after ~3 hours
    public bool RequiresConnectivity => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var azCmd = await GetAzCommandAsync(runner, cancellationToken);
        var result = await runner.RunAsync(azCmd, $"acr login --name {_registryName} --expose-token --query accessToken -o tsv", cancellationToken: cancellationToken);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError)
                ? "No valid ACR token found."
                : result.StandardError.Trim();
            return CheckResult.Failure(error);
        }

        var token = result.StandardOutput.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(token))
            return CheckResult.Failure($"Empty ACR token returned for {_registryName}.");

        // Also verify the token is written and valid in Docker's config.json.
        var host = $"{_registryName}.azurecr.io";
        var dockerToken = ReadDockerAuthToken(host);
        if (dockerToken is null)
            return CheckResult.Failure($"Docker config has no credentials for {host}. Run Fix to write them.");

        if (AuthTokenInspector.TryGetExpiration(dockerToken, out var dockerExpiration) &&
            AuthTokenInspector.IsExpired(dockerExpiration))
            return CheckResult.Failure($"Docker credentials for {host} expired at {dockerExpiration:u}.");

        if (AuthTokenInspector.TryGetExpiration(token, out var expiration))
        {
            if (AuthTokenInspector.IsExpired(expiration))
                return CheckResult.Failure($"ACR token for {_registryName} expired at {expiration:u}.");
            return CheckResult.Success($"ACR token valid (expires {expiration:u}).");
        }

        return CheckResult.Success($"ACR token retrieved for {_registryName}.");
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
        // `az acr login --name` calls `docker login` internally, which writes to the Windows
        // credential store. This fails in non-interactive subprocess sessions with
        // "A specified logon session does not exist". Instead, get the token directly and
        // write it into ~/.docker/config.json, which Docker checks before the credential helper.
        var token = await GetAccessTokenAsync(runner, logger, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return FixResult.Failure(
                "Failed to acquire ACR token. Ensure you are logged in to Azure CLI (az login) " +
                "and have pull access to the registry.");
        }

        var host = $"{_registryName}.azurecr.io";
        var auth = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"00000000-0000-0000-0000-000000000000:{token}"));

        try
        {
            WriteDockerAuth(host, auth, logger);
        }
        catch (Exception ex)
        {
            return FixResult.Failure($"Failed to write Docker auth config: {ex.Message}");
        }

        var verify = await RunAsync(runner, logger, cancellationToken);
        if (!verify.IsSuccessful)
        {
            return FixResult.Failure(verify.Message);
        }

        return FixResult.Success($"Azure ACR credentials written to Docker config for {host}.");
    }

    /// <summary>
    /// Reads the stored JWT from ~/.docker/config.json for the given registry host,
    /// decoding the base64 auth field to extract the password (the token).
    /// Returns null if no entry exists or the entry is malformed.
    /// </summary>
    private static string? ReadDockerAuthToken(string host)
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".docker", "config.json");
            if (!File.Exists(configPath)) return null;

            var root = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject();
            if (root is null) return null;

            var authEntry = root["auths"]?.AsObject()?[host];
            if (authEntry is null) return null;

            var encoded = authEntry["auth"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(encoded)) return null;

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            // format: "00000000-0000-0000-0000-000000000000:<token>"
            var colon = decoded.IndexOf(':');
            return colon >= 0 ? decoded[(colon + 1)..] : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteDockerAuth(string host, string auth, ILogger logger)
    {
        var dockerConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker");
        Directory.CreateDirectory(dockerConfigDir);
        var configPath = Path.Combine(dockerConfigDir, "config.json");

        JsonObject root;
        if (File.Exists(configPath))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        if (!root.ContainsKey("auths"))
        {
            root["auths"] = new JsonObject();
        }

        root["auths"]!.AsObject()[host] = new JsonObject { ["auth"] = auth };

        File.WriteAllText(configPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        logger.LogInformation("Wrote ACR credentials for {Host} to {ConfigPath}", host, configPath);
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

}
