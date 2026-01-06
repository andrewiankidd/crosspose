using System.Collections.Generic;
namespace Crosspose.Doctor;

public sealed class DoctorSettings
{
    public List<string> AdditionalChecks { get; set; } = new();

    public static DoctorSettings Load()
    {
        var config = Crosspose.Core.Configuration.CrossposeConfigurationStore.Load();
        return new DoctorSettings
        {
            AdditionalChecks = config.Doctor.AdditionalChecks ?? new List<string>()
        };
    }

    public static void Save(DoctorSettings settings)
    {
        var config = Crosspose.Core.Configuration.CrossposeConfigurationStore.Load();
        config.Doctor.AdditionalChecks = settings.AdditionalChecks ?? new List<string>();
        Crosspose.Core.Configuration.CrossposeConfigurationStore.Save(config);
    }

    public static string GetSettingsPath() =>
        Crosspose.Core.Configuration.CrossposeConfigurationStore.ConfigPath;
}
