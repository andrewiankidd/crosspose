using System.Collections.Generic;
namespace Crosspose.Doctor.Core;

public sealed class DoctorSettings
{
    public List<string> AdditionalChecks { get; set; } = new();
    public bool OfflineMode { get; set; } = false;

    public static DoctorSettings Load()
    {
        var config = Crosspose.Core.Configuration.CrossposeConfigurationStore.Load();
        return new DoctorSettings
        {
            AdditionalChecks = config.Doctor.AdditionalChecks ?? new List<string>(),
            OfflineMode = config.OfflineMode,
        };
    }

    public static void Save(DoctorSettings settings)
    {
        var config = Crosspose.Core.Configuration.CrossposeConfigurationStore.Load();
        config.Doctor.AdditionalChecks = settings.AdditionalChecks ?? new List<string>();
        config.OfflineMode = settings.OfflineMode;
        Crosspose.Core.Configuration.CrossposeConfigurationStore.Save(config);
    }

    public static bool IsOfflineMode =>
        Crosspose.Core.Configuration.CrossposeConfigurationStore.Load().OfflineMode;

    public static void SetOfflineMode(bool value)
    {
        var config = Crosspose.Core.Configuration.CrossposeConfigurationStore.Load();
        config.OfflineMode = value;
        Crosspose.Core.Configuration.CrossposeConfigurationStore.Save(config);
    }

    public static string GetSettingsPath() =>
        Crosspose.Core.Configuration.CrossposeConfigurationStore.ConfigPath;
}
