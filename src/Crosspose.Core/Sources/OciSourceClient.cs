using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Crosspose.Core.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;

namespace Crosspose.Core.Sources;

public sealed class OciSourceClient : ISourceClient
{
    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<string, Task<string?>> ScopedTokenCache = new(StringComparer.OrdinalIgnoreCase);

    public OciSourceClient(string url, ILogger logger)
    {
        SourceUrl = NormalizeUrl(url);
        SourceName = SourceNameGenerator.Derive(SourceUrl, "oci");
        Logger = logger;
    }

    public string SourceName { get; }
    public string SourceUrl { get; }
    public ILogger Logger { get; }
    public string? NameFilter { get; set; }

    /// <summary>
    /// Lightweight helm check for a single OCI repository (used by GUI before pulling values).
    /// </summary>
    public async Task<bool> IsHelmChartAsync(string repository, SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Helm validation for {Repo}: locating latest tag...", repository);
        var latestTag = await GetLatestTagAsync(repository, auth, cancellationToken);
        if (string.IsNullOrWhiteSpace(latestTag))
        {
            Logger.LogWarning("Helm validation for {Repo} failed: could not determine latest tag.", repository);
            return false;
        }

        Logger.LogInformation("Helm validation for {Repo}: fetching manifest for tag {Tag}...", repository, latestTag);
        var manifest = await GetManifestAsync(repository, latestTag, auth, cancellationToken);
        if (manifest is null)
        {
            Logger.LogWarning("Helm validation for {Repo} failed: manifest retrieval failed.", repository);
            return false;
        }

        if (ContainsHelmManifest(manifest.Value.Body) || ContainsHelmManifestFromHeaders(manifest.Value.ContentType))
        {
            Logger.LogInformation("Helm validation for {Repo} succeeded (tag {Tag}).", repository, latestTag);
            return true;
        }

        Logger.LogInformation("Helm validation for {Repo} failed: manifest for tag {Tag} does not indicate Helm. ContentType={ContentType}", repository, latestTag, manifest.Value.ContentType ?? "(none)");
        return false;
    }

    public async Task<SourceDetectionResult> DetectAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        var catalogUrl = SourceUrl.TrimEnd('/') + "/v2/_catalog?n=1";
        Logger.LogInformation("OCI detection GET {Url}", catalogUrl);
        var result = await SendCatalogRequestAsync(catalogUrl, auth, cancellationToken);
        if (result.IsSuccessStatusCode)
        {
            return new SourceDetectionResult(true);
        }

        if (result.StatusCode == HttpStatusCode.Unauthorized)
        {
            var root = new Uri(SourceUrl).GetLeftPart(UriPartial.Authority) + "/";
            Logger.LogInformation("OCI unauthorized; probing root {Root}", root);
            var rootResp = await Http.GetAsync(root, cancellationToken);
            Logger.LogInformation("OCI root status {Status}", rootResp.StatusCode);
            if (rootResp.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.LogInformation("Treating OCI as private (401 catalog + 404 root).");
                return new SourceDetectionResult(
                    auth is null || string.IsNullOrWhiteSpace(auth.Username)
                        ? false
                        : true,
                    auth is null || string.IsNullOrWhiteSpace(auth.Username)
                        ? "Authentication required."
                        : null,
                    RequiresAuth: true);
            }
            var authResult = SourceAuthHelper.HandleAuthFailure(SourceUrl, auth);
            return new SourceDetectionResult(false, authResult.Message, RequiresAuth: true);
        }

        return new SourceDetectionResult(false, $"OCI detection failed with status {(int)result.StatusCode}");
    }

    public Task<SourceAuthResult> AuthenticateAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SourceAuthHelper.HandleAuthFailure(SourceUrl, auth));
    }

    public async Task<SourceListResult> ListAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        var catalogUrl = SourceUrl.TrimEnd('/') + "/v2/_catalog";
        Logger.LogInformation("OCI listing GET {Url}", catalogUrl);
        var (repos, error) = await ListAllPagedAsync(catalogUrl, auth, cancellationToken);
        if (error is not null)
        {
            return new SourceListResult(false, Array.Empty<SourceChart>(), error);
        }

        var filtered = ApplyFilter(repos, NameFilter);
        var charts = filtered.Select(repo => new SourceChart(repo)).ToList();
        return new SourceListResult(true, charts, null);
    }

    public async Task<SourceVersionResult> ListVersionsAsync(string chartName, SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        var (versions, error) = await ListAllTagsAsync(chartName, auth, cancellationToken);
        if (error is not null)
        {
            return new SourceVersionResult(false, Array.Empty<SourceVersion>(), error);
        }

        return new SourceVersionResult(true, versions, null);
    }

    private static string NormalizeUrl(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;

    private static IReadOnlyList<string> ParseRepositories(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("repositories", out var repos) && repos.ValueKind == JsonValueKind.Array)
            {
                return repos.EnumerateArray()
                    .Select(x => x.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            // Log parse issues for visibility
            System.Diagnostics.Debug.WriteLine($"OCI repo parse failed: {ex.Message}");
        }
        return Array.Empty<string>();
    }

    private async Task<HttpResponseMessage> SendCatalogRequestAsync(string url, SourceAuth? auth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        SourceAuthHelper.ApplyAuth(req, auth);
        var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode != HttpStatusCode.Unauthorized)
        {
            return resp;
        }

        if (!IsAzureContainerRegistry(SourceUrl))
        {
            return resp;
        }

        Logger.LogInformation("OCI catalog unauthorized, attempting ACR scoped token acquisition.");
        var bearer = await TryAcquireAcrScopedTokenAsync("registry:catalog:*", ct);
        if (string.IsNullOrWhiteSpace(bearer))
        {
            return resp;
        }

        using var retryReq = new HttpRequestMessage(HttpMethod.Get, url);
        SourceAuthHelper.ApplyAuth(retryReq, new SourceAuth(null, null, bearer));
        var retryResp = await Http.SendAsync(retryReq, ct);
        return retryResp;
    }

    private async Task<(string Body, string? ContentType)?> GetManifestAsync(string repository, string tag, SourceAuth? auth, CancellationToken ct)
    {
        var encodedRepo = Uri.EscapeDataString(repository);
        var url = $"{SourceUrl.TrimEnd('/')}/v2/{encodedRepo}/manifests/{Uri.EscapeDataString(tag)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.oci.image.manifest.v1+json, application/vnd.cncf.helm.config.v1+json, application/json");
        var resp = await SendRepositoryRequestAsync(repository, req, auth, ct, requireSuccess: true);
        if (resp is null)
        {
            Logger.LogWarning("Manifest request for {Repo} tag {Tag} failed.", repository, tag);
            return null;
        }
        var body = await resp.Content.ReadAsStringAsync(ct);
        Logger.LogInformation("Manifest fetch for {Repo}:{Tag} status {Status}", repository, tag, resp.StatusCode);
        return (body, resp.Content.Headers.ContentType?.MediaType);
    }

    public async Task<(bool Success, string? Values, string? Error)> GetChartValuesAsync(string repository, SourceAuth? auth, CancellationToken ct, string? tag = null)
    {
        var targetTag = string.IsNullOrWhiteSpace(tag)
            ? await GetLatestTagAsync(repository, auth, ct)
            : tag;
        if (string.IsNullOrWhiteSpace(targetTag))
        {
            return (false, null, "Could not determine latest tag.");
        }

        var manifest = await GetManifestAsync(repository, targetTag, auth, ct);
        if (manifest is null)
        {
            return (false, null, "Failed to fetch manifest.");
        }

        string? layerDigest = null;
        try
        {
            using var doc = JsonDocument.Parse(manifest.Value.Body);
            if (doc.RootElement.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
            {
                foreach (var layer in layers.EnumerateArray())
                {
                    var media = layer.GetProperty("mediaType").GetString() ?? string.Empty;
                    if (media.Contains("helm", StringComparison.OrdinalIgnoreCase) ||
                        media.Contains("tar", StringComparison.OrdinalIgnoreCase))
                    {
                        layerDigest = layer.GetProperty("digest").GetString();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse manifest for {Repo}:{Tag}", repository, targetTag);
        }

        if (string.IsNullOrWhiteSpace(layerDigest))
        {
            return (false, null, "No suitable chart layer found in manifest.");
        }

        Logger.LogInformation("Downloading chart blob {Digest} for {Repo}:{Tag}", layerDigest, repository, targetTag);
        var blob = await DownloadBlobAsync(repository, layerDigest, auth, ct);
        if (blob is null)
        {
            return (false, null, "Failed to download chart blob.");
        }

        try
        {
            using var gz = new GZipStream(new MemoryStream(blob), CompressionMode.Decompress);
            using var tar = new TarReader(gz);
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) != null)
            {
                var name = entry.Name.Replace('\\', '/');
                if (name.EndsWith("values.yaml", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("values.yml", StringComparison.OrdinalIgnoreCase))
                {
                    using var ms = new MemoryStream();
                    entry.DataStream?.CopyTo(ms);
                    ms.Position = 0;
                    using var sr = new StreamReader(ms);
                    var content = sr.ReadToEnd();
                    return (true, content, null);
                }
            }
            return (false, null, "values.yaml not found in chart archive.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to extract values.yaml for {Repo}:{Tag}", repository, targetTag);
            return (false, null, "Failed to extract values.yaml.");
        }
    }

    private async Task<string?> GetLatestTagAsync(string repository, SourceAuth? auth, CancellationToken ct)
    {
        var encodedRepo = Uri.EscapeDataString(repository);
        var url = $"{SourceUrl.TrimEnd('/')}/acr/v1/{encodedRepo}/_tags?orderby=timedesc&n=1";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        var resp = await SendRepositoryRequestAsync(repository, req, auth, ct, requireSuccess: true);
        if (resp is null) return null;
        var json = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                var first = tags.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("name", out var name))
                {
                    return name.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse tag listing for {Repo}. Body: {Body}", repository, json);
        }
        return null;
    }

    private async Task<HttpResponseMessage?> SendRepositoryRequestAsync(string repository, HttpRequestMessage request, SourceAuth? auth, CancellationToken ct, bool requireSuccess)
    {
        SourceAuthHelper.ApplyAuth(request, auth);
        var resp = await Http.SendAsync(request, ct);
        if (resp.IsSuccessStatusCode || !IsAzureContainerRegistry(SourceUrl))
        {
            return requireSuccess && !resp.IsSuccessStatusCode ? null : resp;
        }

        if (resp.StatusCode != HttpStatusCode.Unauthorized)
        {
            Logger.LogWarning("Repository request for {Repo} failed with {Status} at {Url}", repository, resp.StatusCode, request.RequestUri);
            return requireSuccess ? null : resp;
        }

        Logger.LogInformation("Repository request unauthorized for {Repo}, attempting scoped token retries.", repository);
        var scopes = new[]
        {
            $"repository:{repository}:metadata_read",
            $"repository:{repository}:pull",
            "registry:catalog:*"
        };

        foreach (var scope in scopes)
        {
            var bearer = await TryAcquireAcrScopedTokenAsync(scope, ct);
            if (string.IsNullOrWhiteSpace(bearer)) continue;

            using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var h in request.Headers)
            {
                retry.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            retry.Content = request.Content is null ? null : new StreamContent(await request.Content.ReadAsStreamAsync(ct));
            SourceAuthHelper.ApplyAuth(retry, new SourceAuth(null, null, bearer));
            var retryResp = await Http.SendAsync(retry, ct);
            if (retryResp.IsSuccessStatusCode) return retryResp;

            Logger.LogWarning("Repository request for {Repo} with scope {Scope} failed with {Status} at {Url}", repository, scope, retryResp.StatusCode, retry.RequestUri);
            if (retryResp.StatusCode != HttpStatusCode.Unauthorized) return requireSuccess ? null : retryResp;
        }

        Logger.LogWarning("Repository request for {Repo} failed after scoped token retries.", repository);
        return null;
    }

    private async Task<byte[]?> DownloadBlobAsync(string repository, string digest, SourceAuth? auth, CancellationToken ct)
    {
        var encodedRepo = Uri.EscapeDataString(repository);
        var url = $"{SourceUrl.TrimEnd('/')}/v2/{encodedRepo}/blobs/{Uri.EscapeDataString(digest)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
        var resp = await SendRepositoryRequestAsync(repository, req, auth, ct, requireSuccess: true);
        if (resp is null)
        {
            Logger.LogWarning("Blob download failed for {Repo} {Digest}", repository, digest);
            return null;
        }

        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<(IReadOnlyList<string> Items, string? Error)> ListAllPagedAsync(string baseUrl, SourceAuth? auth, CancellationToken ct)
    {
        var collected = new List<string>();
        var nextUrl = $"{baseUrl}?n=1000";

        while (nextUrl is not null)
        {
            var resp = await SendCatalogRequestAsync(nextUrl, auth, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            Logger.LogInformation("OCI listing status {Status} for {Url}", resp.StatusCode, nextUrl);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var authResult = SourceAuthHelper.HandleAuthFailure(SourceUrl, auth);
                    return (Array.Empty<string>(), authResult.Message ?? "Authentication required.");
                }
                return (Array.Empty<string>(), $"Listing failed with status {(int)resp.StatusCode}");
            }

            collected.AddRange(ParseRepositories(body));

        if (resp.Headers.TryGetValues("Link", out var links))
        {
            var link = links.FirstOrDefault();
            var next = ParseNextLink(link);
            nextUrl = next is null ? null : new Uri(new Uri(baseUrl), next).ToString();
            }
            else
            {
                nextUrl = null;
            }
        }

        return (collected, null);
    }

    private async Task<(IReadOnlyList<SourceVersion> Versions, string? Error)> ListAllTagsAsync(string repository, SourceAuth? auth, CancellationToken ct)
    {
        var collected = new List<SourceVersion>();
        var encodedRepo = Uri.EscapeDataString(repository);
        var nextUrl = $"{SourceUrl.TrimEnd('/')}/acr/v1/{encodedRepo}/_tags?n=100&orderby=timedesc";

        while (nextUrl is not null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            var resp = await SendRepositoryRequestAsync(repository, req, auth, ct, requireSuccess: true);
            if (resp is null)
            {
                return (Array.Empty<SourceVersion>(), $"Tag listing failed for {repository}.");
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            Logger.LogInformation("OCI tag listing status {Status} for {Repo}", resp.StatusCode, repository);
            if (!resp.IsSuccessStatusCode)
            {
                return (Array.Empty<SourceVersion>(), $"Tag listing failed with status {(int)resp.StatusCode}.");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tagEl in tags.EnumerateArray())
                    {
                        var tag = tagEl.GetProperty("name").GetString();
                        if (string.IsNullOrWhiteSpace(tag)) continue;
                        var digest = tagEl.TryGetProperty("digest", out var digestEl) ? digestEl.GetString() : null;
                        DateTimeOffset? created = null;
                        if (tagEl.TryGetProperty("createdTime", out var createdEl) && createdEl.ValueKind != JsonValueKind.Null)
                        {
                            if (DateTimeOffset.TryParse(createdEl.GetString(), out var parsed))
                            {
                                created = parsed;
                            }
                        }
                        collected.Add(new SourceVersion(tag, digest, created));
                    }
                }

                nextUrl = ResolveNextUrl(resp, doc);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to parse tag listing for {Repo}. Body: {Body}", repository, body);
                return (Array.Empty<SourceVersion>(), "Failed to parse tag listing.");
            }
        }

        return (collected, null);
    }

    private string? ResolveNextUrl(HttpResponseMessage resp, JsonDocument doc)
    {
        string? next = null;
        if (doc.RootElement.TryGetProperty("next", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
        {
            next = nextEl.GetString();
        }

        if (string.IsNullOrWhiteSpace(next) && resp.Headers.TryGetValues("Link", out var links))
        {
            next = ParseNextLink(links.FirstOrDefault());
        }

        if (string.IsNullOrWhiteSpace(next)) return null;

        if (Uri.TryCreate(next, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return new Uri(new Uri(SourceUrl.TrimEnd('/') + "/"), next.TrimStart('/')).ToString();
    }

    private static bool ContainsHelmManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("manifests", out var manifests) && manifests.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in manifests.EnumerateArray())
                {
                    if (ManifestEntryIndicatesHelm(m)) return true;
                }
            }
            if (ManifestEntryIndicatesHelm(root)) return true;
        }
        catch
        {
            // ignore parse errors, treat as non-helm
        }

        return json.IndexOf("vnd.cncf.helm", StringComparison.OrdinalIgnoreCase) >= 0
               || json.IndexOf("helm.chart", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsHelmManifestFromHeaders(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        return contentType.Contains("helm", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("vnd.cncf.helm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ManifestEntryIndicatesHelm(JsonElement element)
    {
        if (element.TryGetProperty("mediaType", out var mt) && mt.GetString()?.Contains("helm", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (element.TryGetProperty("configMediaType", out var cmt) && cmt.GetString()?.Contains("helm", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (element.TryGetProperty("artifactType", out var at) && at.GetString()?.Contains("helm", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (element.TryGetProperty("annotations", out var ann) && ann.ValueKind == JsonValueKind.Object)
        {
            if (ann.TryGetProperty("org.opencontainers.artifact.type", out var aType) &&
                aType.GetString()?.Contains("helm", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        if (element.TryGetProperty("manifest", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            if (ManifestEntryIndicatesHelm(nested)) return true;
        }
        return false;
    }

    private static string? ParseNextLink(string? linkHeader)
    {
        // Example: </v2/_catalog?last=foo&n=100>; rel="next"
        if (string.IsNullOrWhiteSpace(linkHeader)) return null;
        var parts = linkHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var segments = part.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 2 && segments[1].Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
            {
                var urlSegment = segments[0].Trim();
                if (urlSegment.StartsWith("<") && urlSegment.EndsWith(">"))
                {
                    return urlSegment.Trim('<', '>');
                }
            }
        }
        return null;
    }

    private async Task<string?> TryAcquireAcrScopedTokenAsync(string scope, CancellationToken ct)
    {
        // De-duplicate concurrent scope requests.
        var tokenTask = ScopedTokenCache.GetOrAdd(scope, _ => AcquireAcrScopedTokenCoreAsync(scope, ct));
        var token = await tokenTask;

        if (string.IsNullOrWhiteSpace(token))
        {
            ScopedTokenCache.TryRemove(scope, out _);
            return null;
        }

        return token;
    }

    private async Task<string?> AcquireAcrScopedTokenCoreAsync(string scope, CancellationToken ct)
    {
        var host = new Uri(SourceUrl).Host;
        var runner = new Diagnostics.ProcessRunner(Logger);
        var azExe = await ResolveAzCliAsync(runner, ct);
        if (azExe is null)
        {
            Logger.LogWarning("Azure CLI not found on PATH; cannot acquire ACR token.");
            return null;
        }

        var aad = await runner.RunAsync(azExe, "account get-access-token --resource https://management.azure.com/ --query accessToken -o tsv", cancellationToken: ct);
        if (aad.ExitCode != 0 || string.IsNullOrWhiteSpace(aad.StandardOutput))
        {
            Logger.LogWarning("Failed to get AAD token for ACR catalog: {Error}", string.IsNullOrWhiteSpace(aad.StandardError) ? "no output" : aad.StandardError);
            return null;
        }

        var aadToken = aad.StandardOutput.Trim();
        try
        {
            var exchange = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["grant_type"] = "access_token",
                ["service"] = host,
                ["access_token"] = aadToken
            });
            var exchangeResp = await Http.PostAsync($"https://{host}/oauth2/exchange", exchange, ct);
            var exchangeBody = await exchangeResp.Content.ReadAsStringAsync(ct);
            if (!exchangeResp.IsSuccessStatusCode)
            {
                Logger.LogWarning("ACR exchange failed: {Status} {Body}", exchangeResp.StatusCode, exchangeBody);
                return null;
            }

            using var exchangeDoc = JsonDocument.Parse(exchangeBody);
            if (!exchangeDoc.RootElement.TryGetProperty("refresh_token", out var refreshProp))
            {
                Logger.LogWarning("ACR exchange returned no refresh_token.");
                return null;
            }
            var refresh = refreshProp.GetString();
            if (string.IsNullOrWhiteSpace(refresh))
            {
                Logger.LogWarning("ACR exchange refresh_token is empty.");
                return null;
            }

        var tokenReq = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["service"] = host,
            ["scope"] = scope,
            ["refresh_token"] = refresh
        });
        var tokenResp = await Http.PostAsync($"https://{host}/oauth2/token", tokenReq, ct);
            var tokenBody = await tokenResp.Content.ReadAsStringAsync(ct);
            if (!tokenResp.IsSuccessStatusCode)
            {
                Logger.LogWarning("ACR token request failed: {Status} {Body}", tokenResp.StatusCode, tokenBody);
                return null;
            }

            using var tokenDoc = JsonDocument.Parse(tokenBody);
            if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessProp))
            {
                Logger.LogWarning("ACR token response missing access_token.");
                return null;
            }
            var access = accessProp.GetString();
            if (string.IsNullOrWhiteSpace(access))
            {
                Logger.LogWarning("ACR token response access_token empty.");
                return null;
            }
            Logger.LogInformation("ACR token acquired for scope {Scope}.", scope);
            return access;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ACR catalog token acquisition failed.");
            return null;
        }
    }

    private static IReadOnlyList<string> ApplyFilter(IReadOnlyList<string> source, string? filter)
    {
        if (source.Count == 0 || string.IsNullOrWhiteSpace(filter)) return source;
        return source
            .Where(item => item.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    private static bool IsAzureContainerRegistry(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return host.EndsWith(".azurecr.io", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> ResolveAzCliAsync(Diagnostics.ProcessRunner runner, CancellationToken ct)
    {
        // Try az.cmd first (Windows)
        var whereCmd = await runner.RunAsync("where", "az.cmd", cancellationToken: ct);
        if (whereCmd.IsSuccess && !string.IsNullOrWhiteSpace(whereCmd.StandardOutput))
        {
            var path = whereCmd.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        // Fallback to plain az
        var fallback = await runner.RunAsync("where", "az", cancellationToken: ct);
        if (fallback.IsSuccess && !string.IsNullOrWhiteSpace(fallback.StandardOutput))
        {
            var path = fallback.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return null;
    }
}
