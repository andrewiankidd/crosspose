namespace Crosspose.Ui;

public static class DoctorCheckPersistence
{
    public static void EnsureAdditionalChecks(params string[] keys)
    {
        if (keys is null || keys.Length == 0) return;
        Crosspose.Core.Configuration.DoctorCheckRegistrar.EnsureChecks(keys);
    }
}
