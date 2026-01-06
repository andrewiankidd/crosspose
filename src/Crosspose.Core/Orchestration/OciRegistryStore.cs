using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Crosspose.Core.Configuration;

namespace Crosspose.Core.Orchestration;

public sealed class OciRegistryStore
{
    private readonly ILogger _logger;
    private static readonly HttpClient Http = new();
    private readonly CrossposeConfiguration _config;
    private readonly List<OciRegistryEntry> _registries;

    public OciRegistryStore(ILogger logger)
    {
        _logger = logger;
        _config = CrossposeConfigurationStore.Load();
        _config.OciRegistries ??= new List<OciRegistryEntry>();
        _registries = _config.OciRegistries;
    }

    public IReadOnlyList<OciRegistryEntry> GetAll() => _registries.ToList();

    public void AddOrUpdate(OciRegistryEntry entry)
    {
        var existing = _registries.FirstOrDefault(r => r.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Address = entry.Address;
            existing.Username = entry.Username;
            existing.Password = entry.Password;
            existing.BearerToken = entry.BearerToken;
            existing.Filter = entry.Filter;
        }
        else
        {
            entry.Filter = string.IsNullOrWhiteSpace(entry.Filter) ? null : entry.Filter;
            _registries.Add(entry);
        }
        Save();
    }

    private void Save()
    {
        try
        {
            CrossposeConfigurationStore.Save(_config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save OCI registry store.");
        }
    }

    public async Task<IReadOnlyList<string>> ListChartsAsync(OciRegistryEntry entry, CancellationToken cancellationToken = default)
    {
        var url = entry.Address.TrimEnd('/') + "/v2/_catalog";
        var alt = entry.Address.TrimEnd('/') + "/v/2_catalog";
        var repos = await GetCatalogAsync(url, entry, cancellationToken);
        if (repos.Count == 0)
        {
            repos = await GetCatalogAsync(alt, entry, cancellationToken);
        }
        return ApplyFilter(repos, entry.Filter);
    }

    private async Task<List<string>> GetCatalogAsync(string url, OciRegistryEntry entry, CancellationToken token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(entry.BearerToken))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", entry.BearerToken);
            }
            else if (!string.IsNullOrWhiteSpace(entry.Username))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes($"{entry.Username}:{entry.Password ?? string.Empty}");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
            }

            var resp = await Http.SendAsync(req, token);
            if (!resp.IsSuccessStatusCode) return new List<string>();
            var json = await resp.Content.ReadAsStringAsync(token);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("repositories", out var reposElem) && reposElem.ValueKind == JsonValueKind.Array)
            {
                return reposElem.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query OCI catalog at {Url}", url);
        }
        return new List<string>();
    }

    private static List<string> ApplyFilter(List<string> source, string? filter)
    {
        if (source.Count == 0 || string.IsNullOrWhiteSpace(filter)) return source;
        return source
            .Where(item => item.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }
}

public sealed class OciRegistryEntry
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? BearerToken { get; set; }
    public string? Filter { get; set; }
}
