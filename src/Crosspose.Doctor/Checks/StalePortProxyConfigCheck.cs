using Crosspose.Core.Configuration;
using Crosspose.Core.Deployment;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Finds port-proxy entries in crosspose.yml that are no longer referenced by any
/// deployment on disk, and removes them. These accumulate when each Dekompose run
/// produces a new epoch-stamped network name — the old entries are never cleaned up
/// automatically.
///
/// Staleness is determined by scanning deployment directories for conversion-report.yaml
/// files, NOT by querying Docker. Docker networks only exist while a stack is running,
/// so using Docker would cause false positives every time a stack is brought down.
/// </summary>
public sealed class StalePortProxyConfigCheck : ICheckFix
{
    public string Name => "stale-port-proxy-config";
    public string Description => "Removes port-proxy entries from crosspose.yml that are no longer referenced by any deployment on disk.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 300;

    public Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var stale = FindStaleEntries();
        if (stale.Count == 0)
        {
            return Task.FromResult(CheckResult.Success("All port-proxy config entries reference known deployments."));
        }

        return Task.FromResult(CheckResult.Failure(
            $"Found {stale.Count} port-proxy config entry/entries not referenced by any deployment: " +
            string.Join(", ", stale.Select(e => e.Network ?? "(no network)")) + ". Run Fix to remove them."));
    }

    public Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var stale = FindStaleEntries();
        if (stale.Count == 0)
        {
            return Task.FromResult(FixResult.Success("No stale port-proxy config entries to remove."));
        }

        var config = CrossposeConfigurationStore.Load();
        var existing = config.Doctor.AdditionalChecks ?? new List<string>();
        var staleKeys = new HashSet<string>(stale.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);

        var updated = existing.Where(k => !staleKeys.Contains(k)).ToList();
        var removed = existing.Count - updated.Count;

        config.Doctor.AdditionalChecks = updated;
        CrossposeConfigurationStore.Save(config);

        logger.LogInformation("Removed {Count} stale port-proxy config entries", removed);
        return Task.FromResult(FixResult.Success(
            $"Removed {removed} stale port-proxy entry/entries for networks: " +
            string.Join(", ", stale.Select(e => e.Network ?? "(no network)")) + "."));
    }

    private static IReadOnlyList<StaleEntry> FindStaleEntries()
    {
        var config = CrossposeConfigurationStore.Load();
        var checks = config.Doctor.AdditionalChecks ?? new List<string>();

        // Collect all unique network names referenced by port-proxy keys
        var networkKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in checks)
        {
            if (!PortProxyKey.TryParse(key, out _, out _, out var network)) continue;
            if (string.IsNullOrWhiteSpace(network)) continue;

            if (!networkKeys.TryGetValue(network!, out var keyList))
            {
                keyList = new List<string>();
                networkKeys[network!] = keyList;
            }
            keyList.Add(key);
        }

        if (networkKeys.Count == 0) return Array.Empty<StaleEntry>();

        // A network epoch is live if any deployment directory on disk contains a
        // conversion-report.yaml that references it. Docker networks only exist while
        // a stack is running, so querying Docker would cause false positives when stacks
        // are down.
        var liveNetworks = CollectDeploymentNetworks(CrossposeEnvironment.DeploymentDirectory);

        var stale = new List<StaleEntry>();
        foreach (var (network, keys) in networkKeys)
        {
            if (!liveNetworks.Contains(network))
            {
                foreach (var key in keys)
                {
                    stale.Add(new StaleEntry(key, network));
                }
            }
        }

        return stale;
    }

    /// <summary>
    /// Walks all subdirectories of the deployment root and collects every network name
    /// referenced by a conversion-report.yaml found there.
    /// </summary>
    private static HashSet<string> CollectDeploymentNetworks(string deploymentRoot)
    {
        var networks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(deploymentRoot)) return networks;

        foreach (var report in Directory.EnumerateFiles(deploymentRoot, "conversion-report.yaml", SearchOption.AllDirectories))
        {
            try
            {
                var requirements = PortProxyRequirementLoader.Load(Path.GetDirectoryName(report)!);
                foreach (var req in requirements)
                {
                    if (!string.IsNullOrWhiteSpace(req.Network))
                    {
                        networks.Add(req.Network!);
                    }
                }
            }
            catch
            {
                // ignore unreadable reports
            }
        }

        return networks;
    }

    private sealed record StaleEntry(string Key, string? Network);
}
