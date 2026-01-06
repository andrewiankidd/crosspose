namespace Crosspose.Core.Configuration;

public static class ConfigFileLocator
{
    public static string GetConfigPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("fileName must be provided.", nameof(fileName));
        }

        return AppDataLocator.GetPreferredFilePath(fileName);
    }
}
