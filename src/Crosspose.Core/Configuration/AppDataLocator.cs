using System;
using System.IO;

namespace Crosspose.Core.Configuration;

/// <summary>
/// Central helper for resolving files and directories under %APPDATA%\crosspose
/// with a fallback to the executable directory when existing content is found there.
/// </summary>
public static class AppDataLocator
{
    private static readonly string AppDataRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "crosspose");

    private static readonly string LocalRoot =
        AppContext.BaseDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        ?? Directory.GetCurrentDirectory();

    public static string AppDataPath => AppDataRoot;
    public static string LocalPath => LocalRoot;

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

        var localPath = Path.Combine(LocalRoot, normalized);
        if (File.Exists(localPath))
        {
            return localPath;
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

        var localDir = Path.Combine(LocalRoot, normalized);
        if (Directory.Exists(localDir))
        {
            return localDir;
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
}
