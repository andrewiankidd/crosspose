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
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var key in filtered)
        {
            if (set.Add(key))
            {
                changed = true;
            }
        }

        if (changed)
        {
            config.Doctor.AdditionalChecks = set
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
            CrossposeConfigurationStore.Save(config);
        }
    }
}
