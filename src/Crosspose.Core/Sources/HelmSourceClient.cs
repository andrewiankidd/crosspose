using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Core.Sources;

public sealed class HelmSourceClient : ISourceClient
{
    private static readonly HttpClient Http = new();
    private readonly HelmClient? _helmClient;

    public HelmSourceClient(string url, ILogger logger, HelmClient? helmClient = null, string? sourceName = null)
    {
        SourceUrl = NormalizeUrl(url);
        SourceName = string.IsNullOrWhiteSpace(sourceName)
            ? SourceNameGenerator.Derive(SourceUrl, "helm")
            : sourceName;
        Logger = logger;
        _helmClient = helmClient;
    }

    public string SourceName { get; }
    public string SourceUrl { get; }
    public ILogger Logger { get; }

    public async Task<SourceDetectionResult> DetectAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        var url = SourceUrl.TrimEnd('/') + "/index.yaml";
        Logger.LogInformation("Helm detection GET {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        SourceAuthHelper.ApplyAuth(req, auth);
        try
        {
            var resp = await Http.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            Logger.LogInformation("Helm detection status {Status}", resp.StatusCode);
            var ok = resp.IsSuccessStatusCode && body.Contains("apiVersion", StringComparison.OrdinalIgnoreCase);
            return new SourceDetectionResult(ok, ok ? null : $"Helm detection failed with status {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Helm detection failed for {Url}", url);
            return new SourceDetectionResult(false, ex.Message);
        }
    }

    public Task<SourceAuthResult> AuthenticateAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SourceAuthResult(true, "Helm uses basic HTTP retrieval for index.yaml."));

    public async Task<SourceListResult> ListAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        if (_helmClient is null)
        {
            return new SourceListResult(false, Array.Empty<SourceChart>(), "Helm client not provided.");
        }

        try
        {
            Logger.LogInformation("Helm listing charts for source {SourceName}", SourceName);
            var charts = await _helmClient.SearchRepoAsync(SourceName, cancellationToken);
            var list = charts.Select(c => new SourceChart(c.Name, string.IsNullOrWhiteSpace(c.Description) ? null : c.Description)).ToList();
            return new SourceListResult(true, list, null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Helm listing failed for {SourceName}", SourceName);
            return new SourceListResult(false, Array.Empty<SourceChart>(), ex.Message);
        }
    }

    public async Task<SourceVersionResult> ListVersionsAsync(string chartName, SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        if (_helmClient is null)
        {
            return new SourceVersionResult(false, Array.Empty<SourceVersion>(), "Helm client not provided.");
        }

        try
        {
            Logger.LogInformation("Helm listing versions for chart {Chart} on {SourceName}", chartName, SourceName);
            var entries = await _helmClient.SearchChartVersionsAsync(chartName, cancellationToken);
            var versions = entries.Select(e => new SourceVersion(e.Version, Digest: null, CreatedAt: null)).ToList();
            return new SourceVersionResult(true, versions, null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Helm version listing failed for {Chart}", chartName);
            return new SourceVersionResult(false, Array.Empty<SourceVersion>(), ex.Message);
        }
    }

    private static string NormalizeUrl(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
}
