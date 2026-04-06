using System.Globalization;
using System.Text;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Dekompose.Core.Services;

public sealed class HelmTemplateRunner
{
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<HelmTemplateRunner> _logger;

    public HelmTemplateRunner(ProcessRunner processRunner, ILogger<HelmTemplateRunner> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<HelmRenderResult> RenderAsync(string chartPath, string? valuesPath, string outputDirectory, string? chartVersion, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(outputDirectory, $"rendered.{DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.yaml");

        // Normalize OCI references so Helm can pull directly (e.g., oci://registry/repo).
        var effectiveChart = chartPath;
        var isLocalPath = Directory.Exists(chartPath);
        if (!isLocalPath &&
            chartPath.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase) &&
            !chartPath.StartsWith("oci://", StringComparison.OrdinalIgnoreCase))
        {
            effectiveChart = $"oci://{chartPath}";
        }

        IReadOnlyDictionary<string, string>? helmEnv = null;
        string? tempRegistryConfig = null;
        try
        {
            if (effectiveChart.StartsWith("oci://", StringComparison.OrdinalIgnoreCase))
            {
                (helmEnv, tempRegistryConfig) = await BuildOciAuthEnvAsync(effectiveChart, cancellationToken);
            }

            var arguments = $"template crosspose \"{effectiveChart}\"";
            if (!string.IsNullOrWhiteSpace(chartVersion))
            {
                arguments += $" --version \"{chartVersion}\"";
            }

            if (!string.IsNullOrWhiteSpace(valuesPath))
            {
                arguments += $" --values \"{valuesPath}\"";
            }

            string? workDir = Directory.Exists(chartPath) ? chartPath : null;
            var result = await _processRunner.RunAsync("helm", arguments, environment: helmEnv, workingDirectory: workDir, cancellationToken: cancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogError("helm template failed with exit code {ExitCode}: {Error}", result.ExitCode, result.StandardError);
                return new HelmRenderResult(false, manifestPath, false, result.StandardError);
            }

            await File.WriteAllTextAsync(manifestPath, result.StandardOutput, cancellationToken);
            _logger.LogInformation("Rendered manifest written to {ManifestPath}", manifestPath);
            return new HelmRenderResult(true, manifestPath, true, null);
        }
        finally
        {
            if (tempRegistryConfig is not null)
            {
                try { File.Delete(tempRegistryConfig); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Acquires an ACR token and writes a temporary Helm registry config JSON.
    /// Returns environment variables pointing Helm at that config, bypassing the
    /// Windows credential store (which fails in non-interactive subprocess sessions).
    /// </summary>
    private async Task<(IReadOnlyDictionary<string, string> Env, string TempConfigPath)> BuildOciAuthEnvAsync(
        string chartRef, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(chartRef, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return (new Dictionary<string, string>(), string.Empty);
        }

        var host = uri.Host;
        var registryName = host.Split('.')[0];
        _logger.LogInformation("Acquiring ACR token for OCI registry {Registry} ({Host})...", registryName, host);

        var tokenResult = await _processRunner.RunAsync(
            await ResolveAzCliAsync(cancellationToken),
            $"acr login --name {registryName} --expose-token --query accessToken -o tsv",
            cancellationToken: cancellationToken);

        if (!tokenResult.IsSuccess || string.IsNullOrWhiteSpace(tokenResult.StandardOutput))
        {
            _logger.LogError("Failed to acquire ACR token for {Registry}: {Error}", registryName, tokenResult.StandardError);
            throw new InvalidOperationException($"Failed to acquire ACR token for {registryName}");
        }

        var token = tokenResult.StandardOutput.Trim();

        // Build a Docker-compatible registry config JSON with the token inline.
        // This lets Helm authenticate without touching the Windows credential store.
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"00000000-0000-0000-0000-000000000000:{token}"));
        var configJson = "{\"auths\":{\"" + host + "\":{\"auth\":\"" + auth + "\"}}}";

        var tempPath = Path.Combine(Path.GetTempPath(), $"crosspose-helm-auth-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempPath, configJson, cancellationToken);

        _logger.LogInformation("Wrote temporary Helm registry config for {Host}", host);

        var env = new Dictionary<string, string>
        {
            ["HELM_REGISTRY_CONFIG"] = tempPath
        };

        return (env, tempPath);
    }

    private async Task<string> ResolveAzCliAsync(CancellationToken cancellationToken)
    {
        // Prefer az.cmd on Windows
        var azCmd = await _processRunner.RunAsync("where", "az.cmd", cancellationToken: cancellationToken);
        var path = ExtractFirstPath(azCmd.StandardOutput);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        // Fallback to plain az
        var az = await _processRunner.RunAsync("where", "az", cancellationToken: cancellationToken);
        path = ExtractFirstPath(az.StandardOutput);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        _logger.LogError("Azure CLI not found on PATH. Ensure az is installed and available.");
        throw new InvalidOperationException("Azure CLI not found on PATH.");
    }

    private static string? ExtractFirstPath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return input
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }
}

public sealed record HelmRenderResult(bool Succeeded, string RenderedManifestPath, bool UsedHelm, string? Error);
