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
