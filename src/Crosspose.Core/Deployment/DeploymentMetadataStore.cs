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
}
