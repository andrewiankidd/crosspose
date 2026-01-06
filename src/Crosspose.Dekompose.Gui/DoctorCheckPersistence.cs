using System;
using System.Collections.Generic;
using System.Linq;
using Crosspose.Doctor;

namespace Crosspose.Dekompose.Gui;

internal static class DoctorCheckPersistence
{
    public static void EnsureAdditionalChecks(params string[] keys)
    {
        if (keys is null || keys.Length == 0) return;

        Crosspose.Core.Configuration.DoctorCheckRegistrar.EnsureChecks(keys);
    }
}
