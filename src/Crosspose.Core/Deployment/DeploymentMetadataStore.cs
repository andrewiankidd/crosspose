using System;
using System.IO;
using System.Linq;
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
                // Flat layout: <deployBase>/<project>[-suffix]/
                // Derive Project from dir name only as last-resort fallback.
                metadata.Project ??= ExtractProjectFromDirName(Path.GetFileName(directory));
            }
            return metadata;
        }
        catch
        {
            return new DeploymentMetadata
            {
                Project = ExtractProjectFromDirName(Path.GetFileName(directory))
            };
        }
    }

    public static void Write(string directory, DeploymentMetadata metadata)
    {
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));
        metadata.Project ??= ExtractProjectFromDirName(Path.GetFileName(directory));
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
    /// Returns the deployment directory whose name matches <paramref name="composeProject"/>
    /// (the Docker/Podman compose project label, which equals the directory leaf name).
    /// Returns the most recently written match if multiple exist (collision-suffixed duplicates).
    /// </summary>
    public static string? FindDeploymentDirectory(string composeProject)
    {
        if (string.IsNullOrWhiteSpace(composeProject)) return null;
        var deployBase = Configuration.CrossposeEnvironment.DeploymentDirectory;
        if (!Directory.Exists(deployBase)) return null;

        // Flat layout: directory name == compose project name.
        return Directory.EnumerateDirectories(deployBase)
            .Where(d => Path.GetFileName(d).Equals(composeProject, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the directory leaf name as the project name. The directory name IS the project name
    /// under the current flat layout (e.g. "helm-platform" or "helm-platform-1" for collision).
    /// </summary>
    public static string ExtractProjectFromDirName(string dirName) =>
        string.IsNullOrWhiteSpace(dirName) ? dirName : dirName;
}
