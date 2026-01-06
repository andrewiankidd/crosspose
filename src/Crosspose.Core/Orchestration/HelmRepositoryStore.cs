using System.Collections.Generic;
using System.Linq;
using Crosspose.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Core.Orchestration;

public sealed class HelmRepositoryStore
{
    private readonly ILogger _logger;
    private readonly CrossposeConfiguration _config;
    private readonly List<HelmRepositoryEntry> _repositories;

    public HelmRepositoryStore(ILogger logger)
    {
        _logger = logger;
        _config = CrossposeConfigurationStore.Load();
        _config.HelmRepositories ??= new List<HelmRepositoryEntry>();
        _repositories = _config.HelmRepositories;
    }

    public IReadOnlyList<HelmRepositoryEntry> GetAll() => _repositories.ToList();

    public string? GetFilter(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _repositories.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Filter;
    }

    public void SetFilter(string name, string? filter)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var existing = _repositories.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(filter))
        {
            if (existing is not null)
            {
                _repositories.Remove(existing);
                Save();
            }
            return;
        }

        if (existing is null)
        {
            existing = new HelmRepositoryEntry { Name = name };
            _repositories.Add(existing);
        }

        existing.Filter = filter;
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
            _logger.LogWarning(ex, "Failed to save Helm repository filters.");
        }
    }
}
