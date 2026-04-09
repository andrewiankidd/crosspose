using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Crosspose.Core.Configuration;
using Crosspose.Core.Sources;

namespace Crosspose.Core.Orchestration;

public sealed class OciRegistryStore
{
    private readonly ILogger _logger;
    private readonly CrossposeConfiguration _config;
    private readonly List<OciRegistryEntry> _registries;

    // Shipped with Crosspose as a PoC / getting-started reference.
    internal static readonly OciRegistryEntry HelloWorldDefault = new()
    {
        Name = "oci-ghcr-io-andrewiankidd-charts",
        Address = "https://ghcr.io",
        Filter = "andrewiankidd/charts/cross-platform-hello"
    };

    public OciRegistryStore(ILogger logger)
    {
        _logger = logger;
        _config = CrossposeConfigurationStore.Load();
        _config.OciRegistries ??= new List<OciRegistryEntry>();
        _registries = _config.OciRegistries;
        EnsureDefaults();
    }

    private void EnsureDefaults()
    {
        if (_registries.Any(r => r.Name.Equals(HelloWorldDefault.Name, StringComparison.OrdinalIgnoreCase)))
            return;
        _registries.Add(HelloWorldDefault);
        Save();
    }

    public IReadOnlyList<OciRegistryEntry> GetAll() => _registries.ToList();

    /// <summary>
    /// Returns the configured registry entry whose address matches <paramref name="registryHost"/>
    /// (e.g. "amcsmainprdcr.azurecr.io"), or null if no match is found.
    /// </summary>
    public OciRegistryEntry? TryGetEntryForHost(string registryHost)
    {
        if (string.IsNullOrWhiteSpace(registryHost)) return null;
        return _registries.FirstOrDefault(r =>
        {
            var addr = r.Address;
            if (!addr.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                addr = "https://" + addr;
            return Uri.TryCreate(addr, UriKind.Absolute, out var uri)
                && uri.Host.Equals(registryHost, StringComparison.OrdinalIgnoreCase);
        });
    }

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
        var auth = new SourceAuth(entry.Username, entry.Password, entry.BearerToken);
        var client = new OciSourceClient(entry.Address, _logger) { NameFilter = entry.Filter };
        var result = await client.ListAsync(auth, cancellationToken);
        return result.Items.Select(c => c.Name).ToList();
    }

    /// <summary>
    /// Fetches all available tags for a container image repository (e.g. "erp-services/platform-erp-service").
    /// Matches against configured OCI registries by hostname. Returns an empty list if no registry is
    /// found or the fetch fails.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListTagsForRepositoryAsync(string registry, string repository, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository)) return Array.Empty<string>();

        OciRegistryEntry? entry = null;
        if (!string.IsNullOrWhiteSpace(registry))
        {
            entry = _registries.FirstOrDefault(r =>
            {
                var addr = r.Address;
                if (!addr.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    addr = "https://" + addr;
                if (Uri.TryCreate(addr, UriKind.Absolute, out var uri))
                    return uri.Host.Equals(registry, StringComparison.OrdinalIgnoreCase);
                return false;
            });
        }

        var registryUrl = entry?.Address
            ?? (registry.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? registry : "https://" + registry);
        var auth = entry is not null
            ? new SourceAuth(entry.Username, entry.Password, entry.BearerToken)
            : null;

        try
        {
            var client = new OciSourceClient(registryUrl, _logger);
            var result = await client.ListVersionsAsync(repository, auth, cancellationToken);
            return result.IsSuccess
                ? result.Versions.Select(v => v.Tag).Where(t => !string.IsNullOrWhiteSpace(t)).ToList()!
                : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list tags for {Registry}/{Repo}", registry, repository);
            return Array.Empty<string>();
        }
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
