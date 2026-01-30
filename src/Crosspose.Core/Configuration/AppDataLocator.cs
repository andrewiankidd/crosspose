using System;
using System.IO;

namespace Crosspose.Core.Configuration;

/// <summary>
/// Central helper for resolving files and directories under %APPDATA%\crosspose.
/// When a ".portable" marker exists beside the executable, all data is redirected
/// to a local AppData\crosspose folder next to the app and legacy data is migrated.
/// </summary>
public static class AppDataLocator
{
    private static readonly string LocalRoot =
        AppContext.BaseDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        ?? Directory.GetCurrentDirectory();

    private static readonly bool IsPortable =
        File.Exists(Path.Combine(LocalRoot, ".portable"));

    private static readonly string LegacyRoamingRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "crosspose");

    private static readonly string LegacyLocalRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Crosspose");

    private static readonly string AppDataRoot =
        IsPortable
            ? Path.Combine(LocalRoot, "AppData", "crosspose")
            : LegacyRoamingRoot;

    static AppDataLocator()
    {
        if (IsPortable)
        {
            TryMigratePortableData();
        }
    }

    public static string AppDataPath => AppDataRoot;
    public static string LocalPath => LocalRoot;
    public static bool IsPortableMode => IsPortable;

    public static string WithPortableSuffix(string title)
    {
        if (!IsPortable) return title;
        if (string.IsNullOrWhiteSpace(title)) return "[Portable Mode]";
        return title.Contains("[Portable Mode]", StringComparison.OrdinalIgnoreCase)
            ? title
            : $"{title} [Portable Mode]";
    }

    public static string GetPreferredFilePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path must be provided.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            EnsureParentDirectory(relativePath);
            return relativePath;
        }

        var normalized = TrimSeparators(relativePath);
        var appDataPath = Path.Combine(AppDataRoot, normalized);
        if (File.Exists(appDataPath))
        {
            return appDataPath;
        }

        if (!IsPortable)
        {
            var localPath = Path.Combine(LocalRoot, normalized);
            if (File.Exists(localPath))
            {
                return localPath;
            }
        }

        EnsureParentDirectory(appDataPath);
        return appDataPath;
    }

    public static string GetPreferredDirectory(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path must be provided.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            Directory.CreateDirectory(relativePath);
            return relativePath;
        }

        var normalized = TrimSeparators(relativePath);
        var appDataDir = Path.Combine(AppDataRoot, normalized);
        if (Directory.Exists(appDataDir))
        {
            return appDataDir;
        }

        if (!IsPortable)
        {
            var localDir = Path.Combine(LocalRoot, normalized);
            if (Directory.Exists(localDir))
            {
                return localDir;
            }
        }

        Directory.CreateDirectory(appDataDir);
        return appDataDir;
    }

    private static string TrimSeparators(string value) =>
        value.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void TryMigratePortableData()
    {
        try
        {
            // Move roaming config/data into portable AppData if the target doesn't exist.
            if (Directory.Exists(LegacyRoamingRoot) && !Directory.Exists(AppDataRoot))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppDataRoot)!);
                Directory.Move(LegacyRoamingRoot, AppDataRoot);
            }

            // Move legacy LocalAppData helm folder into portable AppData if present.
            var legacyHelm = Path.Combine(LegacyLocalRoot, "helm");
            var portableHelm = Path.Combine(AppDataRoot, "helm");
            if (Directory.Exists(legacyHelm) && !Directory.Exists(portableHelm))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(portableHelm)!);
                Directory.Move(legacyHelm, portableHelm);
            }
        }
        catch
        {
            // Best-effort migration; ignore failures to avoid blocking startup.
        }
    }
}
