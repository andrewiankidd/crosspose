using System.Globalization;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Dekompose.Services;

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

        if (effectiveChart.StartsWith("oci://", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureOciLoginAsync(effectiveChart, cancellationToken);
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

        // If the chart path is a local folder, we can use it as the working directory; otherwise leave the default.
        string? workDir = Directory.Exists(chartPath) ? chartPath : null;
        var result = await _processRunner.RunAsync("helm", arguments, workingDirectory: workDir, cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            _logger.LogError("helm template failed with exit code {ExitCode}: {Error}", result.ExitCode, result.StandardError);
            return new HelmRenderResult(false, manifestPath, false, result.StandardError);
        }

        await File.WriteAllTextAsync(manifestPath, result.StandardOutput, cancellationToken);
        _logger.LogInformation("Rendered manifest written to {ManifestPath}", manifestPath);
        return new HelmRenderResult(true, manifestPath, true, null);
    }

    private async Task EnsureOciLoginAsync(string chartRef, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(chartRef, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return;
        }

        var host = uri.Host;
        var registryName = host.Split('.')[0];
        _logger.LogInformation("Ensuring Helm is logged in to OCI registry {Registry} ({Host})...", registryName, host);

        var tokenResult = await _processRunner.RunAsync(
            await ResolveAzCliAsync(cancellationToken),
            $"acr login --name {registryName} --expose-token --query accessToken -o tsv",
            cancellationToken: cancellationToken);

        if (!tokenResult.IsSuccess || string.IsNullOrWhiteSpace(tokenResult.StandardOutput))
        {
            _logger.LogError("Failed to acquire ACR token for {Registry}: {Error}", registryName, tokenResult.StandardError);
            throw new InvalidOperationException($"Failed to acquire ACR token for {registryName}");
        }

        var password = tokenResult.StandardOutput.Trim();
        var loginResult = await _processRunner.RunAsync(
            "helm",
            $"registry login {host} --username 00000000-0000-0000-0000-000000000000 --password \"{password}\"",
            cancellationToken: cancellationToken);

        if (!loginResult.IsSuccess)
        {
            _logger.LogError("Helm registry login failed for {Host}: {Error}", host, loginResult.StandardError);
            throw new InvalidOperationException($"Helm registry login failed for {host}");
        }

        _logger.LogInformation("Helm registry login succeeded for {Host}", host);
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
