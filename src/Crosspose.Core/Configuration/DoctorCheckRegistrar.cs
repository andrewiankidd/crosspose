using System;
using System.Collections.Generic;
using System.Linq;

namespace Crosspose.Core.Configuration;

public static class DoctorCheckRegistrar
{
    public static void EnsureChecks(params string[] keys) => EnsureChecks((IEnumerable<string>?)keys);

    public static void EnsureChecks(IEnumerable<string>? keys)
    {
        if (keys is null) return;

        var filtered = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (filtered.Count == 0) return;

        var config = CrossposeConfigurationStore.Load();
        var existing = config.Doctor.AdditionalChecks ?? new List<string>();

        // For port-proxy keys, replace any existing entry with the same listen port
        // regardless of network name or connect port. Each Dekompose run may produce
        // different connect ports and network names — keep only the most recent.
        var result = new List<string>(existing);
        var changed = false;

        foreach (var key in filtered)
        {
            if (PortProxyKey.TryParse(key, out var listenPort, out _, out _))
            {
                // Remove all existing port-proxy entries for this listen port
                var toRemove = result
                    .Where(e => PortProxyKey.TryParse(e, out var existingPort, out _, out _)
                                && existingPort == listenPort)
                    .ToList();

                foreach (var stale in toRemove)
                {
                    result.Remove(stale);
                    changed = true;
                }
            }

            if (!result.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(key);
                changed = true;
            }
        }

        if (changed)
        {
            config.Doctor.AdditionalChecks = result
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
            CrossposeConfigurationStore.Save(config);
        }
    }
}
