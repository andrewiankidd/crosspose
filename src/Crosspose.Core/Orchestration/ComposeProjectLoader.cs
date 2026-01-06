using System.IO.Compression;

namespace Crosspose.Core.Orchestration;

public enum ComposePlatform
{
    Docker,
    Podman
}

public sealed record ComposeFileEntry(string Workload, string Os, ComposePlatform Platform, string FullPath);

public sealed class ComposeProjectLayout : IDisposable
{
    public ComposeProjectLayout(string rootPath, string projectName, IReadOnlyList<ComposeFileEntry> windowsFiles, IReadOnlyList<ComposeFileEntry> linuxFiles, bool cleanupOnDispose)
    {
        RootPath = rootPath;
        ProjectName = projectName;
        WindowsFiles = windowsFiles;
        LinuxFiles = linuxFiles;
        _cleanupOnDispose = cleanupOnDispose;
    }

    private readonly bool _cleanupOnDispose;

    public string RootPath { get; }
    public string ProjectName { get; }
    public IReadOnlyList<ComposeFileEntry> WindowsFiles { get; }
    public IReadOnlyList<ComposeFileEntry> LinuxFiles { get; }

    public void Dispose()
    {
        if (_cleanupOnDispose && Directory.Exists(RootPath))
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}

public static class ComposeProjectLoader
{
    public static ComposeProjectLayout Load(string path, string? workloadFilter = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path must be provided.", nameof(path));

        var (resolvedPath, cleanup) = PrepareDirectory(path);
        var composeFiles = EnumerateComposeFiles(resolvedPath).ToList();
        if (composeFiles.Count == 0)
        {
            var candidateDirs = Directory.GetDirectories(resolvedPath)
                .OrderByDescending(Directory.GetLastWriteTime)
                .ToList();
            foreach (var dir in candidateDirs)
            {
                var files = EnumerateComposeFiles(dir).ToList();
                if (files.Count > 0)
                {
                    composeFiles = files;
                    resolvedPath = dir;
                    break;
                }
            }
        }

        if (composeFiles.Count == 0)
        {
            if (cleanup && Directory.Exists(resolvedPath))
            {
                Directory.Delete(resolvedPath, recursive: true);
            }
            throw new DirectoryNotFoundException($"No docker-compose.*.yml files found under {path}.");
        }

        var windows = composeFiles.Where(f => f.Platform == ComposePlatform.Docker).ToList();
        var linux = composeFiles.Where(f => f.Platform == ComposePlatform.Podman).ToList();
        if (!string.IsNullOrWhiteSpace(workloadFilter))
        {
            windows = windows.Where(f => f.Workload.Equals(workloadFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            linux = linux.Where(f => f.Workload.Equals(workloadFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (windows.Count == 0 && linux.Count == 0)
            {
                throw new InvalidOperationException($"No compose files found for workload '{workloadFilter}' in {resolvedPath}.");
            }
        }

        var projectName = Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return new ComposeProjectLayout(resolvedPath, projectName, windows, linux, cleanup);
    }

    private static (string Path, bool Cleanup) PrepareDirectory(string path)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
        {
            return (path, false);
        }

        if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(Path.GetTempPath(), "crosspose", "compose", Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(path, temp, overwriteFiles: true);
            return (temp, true);
        }

        throw new DirectoryNotFoundException($"Compose directory '{path}' not found.");
    }

    private static IEnumerable<ComposeFileEntry> EnumerateComposeFiles(string directory)
    {
        var files = Directory.GetFiles(directory, "docker-compose.*.yml", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            var workload = parts[^2];
            var os = parts[^1];
            var platform = os.StartsWith("win", StringComparison.OrdinalIgnoreCase)
                ? ComposePlatform.Docker
                : ComposePlatform.Podman;
            yield return new ComposeFileEntry(workload, os, platform, Path.GetFullPath(file));
        }
    }
}
