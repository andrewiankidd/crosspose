using System;
using System.IO;
using System.Text.Json;

namespace Crosspose.Core.Deployment;

public static class DeploymentMetadataStore
{
    public const string MetadataFileName = ".crosspose-deployment.yml";

    public static DeploymentMetadata? Read(string directory)
    {
        var path = Path.Combine(directory, MetadataFileName);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            var metadata = JsonSerializer.Deserialize<DeploymentMetadata>(json);
            if (metadata is not null)
            {
                metadata.Project ??= Path.GetFileName(Path.GetDirectoryName(directory)?.TrimEnd(Path.DirectorySeparatorChar) ?? directory);
                metadata.Version ??= Path.GetFileName(directory);
            }
            return metadata;
        }
        catch
        {
            return new DeploymentMetadata
            {
                Project = Path.GetFileName(Path.GetDirectoryName(directory)?.TrimEnd(Path.DirectorySeparatorChar) ?? directory),
                Version = Path.GetFileName(directory)
            };
        }
    }

    public static void Write(string directory, DeploymentMetadata metadata)
    {
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));
        metadata.Project ??= Path.GetFileName(Path.GetDirectoryName(directory)?.TrimEnd(Path.DirectorySeparatorChar) ?? directory);
        metadata.Version ??= Path.GetFileName(directory);
        metadata.LastUpdatedUtc = DateTime.UtcNow;

        var path = Path.Combine(directory, MetadataFileName);
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? directory);
        File.WriteAllText(path, json);
    }

    public static void Update(string directory, Action<DeploymentMetadata> update)
    {
        if (update is null) throw new ArgumentNullException(nameof(update));
        var metadata = Read(directory) ?? new DeploymentMetadata();
        update(metadata);
        Write(directory, metadata);
    }

    /// <summary>
    /// Returns the most recent deployment directory for <paramref name="project"/>, or null if
    /// no matching deployment exists under <see cref="Configuration.CrossposeEnvironment.DeploymentDirectory"/>.
    /// When multiple version subdirectories exist, the one with the latest last-write time is returned.
    /// </summary>
    public static string? FindDeploymentDirectory(string project)
    {
        if (string.IsNullOrWhiteSpace(project)) return null;
        var deployBase = Configuration.CrossposeEnvironment.DeploymentDirectory;
        if (!Directory.Exists(deployBase)) return null;

        // Layout: <deployBase>/<project-name>/<version>/
        var projectRoot = Path.Combine(deployBase, project);
        if (Directory.Exists(projectRoot))
        {
            // Return the most recent version subfolder
            return Directory.EnumerateDirectories(projectRoot)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        // Fallback: search one level deeper for a case-insensitive match
        return Directory.EnumerateDirectories(deployBase)
            .Where(d => Path.GetFileName(d).Equals(project, StringComparison.OrdinalIgnoreCase))
            .SelectMany(d => Directory.EnumerateDirectories(d))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
