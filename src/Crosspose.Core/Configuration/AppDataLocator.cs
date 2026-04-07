using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Crosspose.Core.Configuration;

/// <summary>
/// Central helper for resolving files and directories under %APPDATA%\crosspose.
/// When a ".portable" marker exists beside the executable, all data is redirected
/// to a local AppData\crosspose folder next to the app and legacy data is migrated.
/// </summary>
public static class AppDataLocator
{
    private static readonly string LocalRoot =
        ResolveLocalRoot();

    private static readonly bool IsPortable =
        DetectPortableMode(LocalRoot);

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

    [SupportedOSPlatform("windows")]
    private static readonly bool IsElevated =
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    [SupportedOSPlatform("windows")]
    public static bool IsRunningElevated => IsElevated;

    [SupportedOSPlatform("windows")]
    public static string WithPortableSuffix(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) title = "Crosspose";

        if (IsPortable && !title.Contains("[Portable]", StringComparison.OrdinalIgnoreCase))
        {
            title = $"{title} [Portable]";
        }

        if (IsElevated && !title.Contains("[Administrator]", StringComparison.OrdinalIgnoreCase))
        {
            title = $"{title} [Administrator]";
        }

        return title;
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

    /// <summary>
    /// Sets the CROSSPOSE_PORTABLE_ROOT environment variable so child processes
    /// inherit portable mode without needing their own .portable marker.
    /// Call this from the main GUI process after detecting portable mode.
    /// </summary>
    public static void PropagatePortableMode()
    {
        if (IsPortable)
        {
            Environment.SetEnvironmentVariable("CROSSPOSE_PORTABLE_ROOT", LocalRoot, EnvironmentVariableTarget.Process);
        }
    }

    private static string ResolveLocalRoot()
    {
        // If a parent process set CROSSPOSE_PORTABLE_ROOT, use that as our root.
        // This ensures child processes (Doctor.Gui, CLI tools launched from GUI)
        // see the same portable root regardless of their own AppContext.BaseDirectory.
        var envRoot = Environment.GetEnvironmentVariable("CROSSPOSE_PORTABLE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
            return envRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return AppContext.BaseDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            ?? Directory.GetCurrentDirectory();
    }

    private static bool DetectPortableMode(string root)
    {
        // Explicit env var from parent process — GUI sets this so child processes
        // (Doctor.Gui, Dekompose.Gui, CLI tools) inherit portable mode.
        var envRoot = Environment.GetEnvironmentVariable("CROSSPOSE_PORTABLE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
            return true;

        // Direct .portable marker next to the executable
        return File.Exists(Path.Combine(root, ".portable"));
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
