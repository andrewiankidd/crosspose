using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Core.Orchestration;

public sealed class HelmClient
{
    private readonly ProcessRunner _runner;
    private readonly ILogger _logger;
    private string? _helmPath;
    private static readonly HttpClient Http = new();
    private const string HelmVersion = "v3.15.4";

    public HelmClient(ProcessRunner runner, ILogger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task EnsureHelmAsync(CancellationToken cancellationToken = default)
    {
        if (_helmPath is not null && File.Exists(_helmPath)) return;

        // Try PATH first
        var versionCheck = await _runner.RunAsync("helm", "version --short", cancellationToken: cancellationToken);
        if (versionCheck.IsSuccess)
        {
            _helmPath = "helm";
            return;
        }

        // Download to local tools folder
        var toolsDir = AppDataLocator.GetPreferredDirectory("helm");
        Directory.CreateDirectory(toolsDir);
        var targetExe = Path.Combine(toolsDir, "helm.exe");
        if (File.Exists(targetExe))
        {
            _helmPath = targetExe;
            return;
        }

        var url = $"https://get.helm.sh/helm-{HelmVersion}-windows-amd64.zip";
        var tmpZip = Path.Combine(Path.GetTempPath(), $"helm-{HelmVersion}.zip");
        _logger.LogInformation("Downloading Helm {Version} from {Url}", HelmVersion, url);
        using (var resp = await Http.GetAsync(url, cancellationToken))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(tmpZip);
            await resp.Content.CopyToAsync(fs, cancellationToken);
        }

        _logger.LogInformation("Extracting Helm to {Dir}", toolsDir);
        ZipFile.ExtractToDirectory(tmpZip, toolsDir, overwriteFiles: true);
        var extracted = Path.Combine(toolsDir, "windows-amd64", "helm.exe");
        if (File.Exists(extracted))
        {
            File.Copy(extracted, targetExe, overwrite: true);
            _helmPath = targetExe;
        }
        else if (File.Exists(targetExe))
        {
            _helmPath = targetExe;
        }
        else
        {
            throw new FileNotFoundException("Helm download did not produce helm.exe");
        }
    }

    private string HelmCommand => _helmPath ?? "helm";

    public Task<ProcessResult> RepoUpdateAsync(CancellationToken cancellationToken = default) =>
        _runner.RunAsync(HelmCommand, "repo update", cancellationToken: cancellationToken);

    public Task<ProcessResult> RepoAddAsync(string name, string url, string? username = null, string? password = null, CancellationToken cancellationToken = default)
    {
        var args = $"repo add {name} \"{url}\"";
        if (!string.IsNullOrWhiteSpace(username))
        {
            args += $" --username \"{username}\"";
        }
        if (!string.IsNullOrWhiteSpace(password))
        {
            args += $" --password \"{password}\"";
        }
        return _runner.RunAsync(HelmCommand, args, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<HelmRepoEntry>> RepoListAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(HelmCommand, "repo list -o json", cancellationToken: cancellationToken);
        return ParseArray<HelmRepoEntry>(result.StandardOutput);
    }

    public async Task<IReadOnlyList<HelmChartSearchEntry>> SearchRepoAsync(string repoName, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(HelmCommand, $"search repo {repoName} -o json", cancellationToken: cancellationToken);
        return ParseArray<HelmChartSearchEntry>(result.StandardOutput);
    }

    public async Task<IReadOnlyList<HelmChartSearchEntry>> SearchChartVersionsAsync(string chartFullName, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(HelmCommand, $"search repo {chartFullName} --versions -o json", cancellationToken: cancellationToken);
        return ParseArray<HelmChartSearchEntry>(result.StandardOutput);
    }

    public async Task<string> ShowValuesAsync(string chartFullName, string? version, CancellationToken cancellationToken = default)
    {
        var args = $"show values {chartFullName}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            args += $" --version {version}";
        }
        var result = await _runner.RunAsync(HelmCommand, args, cancellationToken: cancellationToken);
        return result.StandardOutput ?? string.Empty;
    }

    /// <summary>
    /// Pulls a chart tgz into <paramref name="destinationDir"/> and returns the path of the
    /// downloaded file, or null if the pull failed.
    /// </summary>
    public async Task<string?> PullAsync(string chartRef, string? version, string destinationDir, Sources.SourceAuth? auth = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDir);
        var before = Directory.GetFiles(destinationDir, "*.tgz").ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Both Docker's credsStore ("desktop") and Helm's credsStore ("wincred") route credential
        // lookups through Windows Credential Manager, which fails in subprocess sessions with
        // "A specified logon session does not exist". Bypass entirely by writing a temp registry
        // config with no credsStore and pointing HELM_REGISTRY_CONFIG at it.
        Dictionary<string, string>? env = null;
        string? tempConfig = null;
        if (chartRef.StartsWith("oci://", StringComparison.OrdinalIgnoreCase) && auth is not null)
        {
            var password = auth.BearerToken ?? auth.Password;
            var username = auth.Username ?? (string.IsNullOrWhiteSpace(password) ? null : "00000000-0000-0000-0000-000000000000");
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                var host = new Uri("https://" + chartRef["oci://".Length..]).Host;
                var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
                var json = "{\"auths\":{\"" + host + "\":{\"auth\":\"" + b64 + "\"}}}";
                tempConfig = Path.Combine(Path.GetTempPath(), $"helm-reg-{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(tempConfig, json, cancellationToken);
                env = new Dictionary<string, string> { ["HELM_REGISTRY_CONFIG"] = tempConfig };
            }
        }

        var args = $"pull \"{chartRef}\" --destination \"{destinationDir}\"";
        if (!string.IsNullOrWhiteSpace(version))
            args += $" --version \"{version}\"";

        var result = await _runner.RunAsync(HelmCommand, args, environment: env, cancellationToken: cancellationToken);
        if (tempConfig is not null) try { File.Delete(tempConfig); } catch { }
        if (!result.IsSuccess)
        {
            _logger.LogWarning("helm pull failed: {Error}", result.StandardError);
            return null;
        }

        var after = Directory.GetFiles(destinationDir, "*.tgz");
        return after.FirstOrDefault(f => !before.Contains(f))
            ?? after.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private IReadOnlyList<T> ParseArray<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<T>();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<T>>(json, options) ?? new List<T>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse helm json output.");
            return Array.Empty<T>();
        }
    }
}

public sealed class HelmRepoEntry
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class HelmChartSearchEntry
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string App_Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Display => string.IsNullOrWhiteSpace(Version) ? Name : $"{Name} ({Version})";
}
