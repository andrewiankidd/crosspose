using Crosspose.Core.Diagnostics;
using System.Text.Json;

namespace Crosspose.Core.Orchestration;

public sealed class DockerContainerRunner : ContainerPlatformRunnerBase
{
    public DockerContainerRunner(ProcessRunner runner) : base("docker", runner)
    {
    }

    public override Task<PlatformCommandResult> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "ps", "--no-trunc", "--format", "json" };
        if (includeAll)
        {
            args.Insert(1, "-a");
        }

        return ExecuteAndWrapAsync(args, cancellationToken);
    }

    public override async Task<IReadOnlyList<ContainerProcessInfo>> GetContainersDetailedAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "ps", "--no-trunc", "--format", "json" };
        if (includeAll)
        {
            args.Insert(1, "-a");
        }

        var result = await ExecAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<ContainerProcessInfo>();
        }

        var containers = new List<ContainerProcessInfo>();
        foreach (var element in EnumerateJsonElements(result.StandardOutput))
        {
            try
            {
                var root = element;
                var id = root.GetPropertyOrDefault("ID");
                var names = root.GetPropertyOrDefault("Names");
                var image = root.GetPropertyOrDefault("Image");
                var status = root.GetPropertyOrDefault("Status");
                var state = root.GetPropertyOrDefault("State");
                var ports = root.GetPropertyOrDefault("Ports");
                var labels = root.GetPropertyOrDefault("Labels");
                string? project = null;
                if (!string.IsNullOrWhiteSpace(labels))
                {
                    var parts = labels.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var kv in parts)
                    {
                        var trimmed = kv.Trim();
                        if (trimmed.StartsWith("com.docker.compose.project=", StringComparison.OrdinalIgnoreCase))
                        {
                            project = trimmed[(trimmed.IndexOf('=') + 1)..];
                            break;
                        }
                    }
                }

                if (project is null)
                {
                    Runner.LogDebug("Docker container missing compose project label. ID={Id} Names={Names} Labels={Labels}", id, names, labels);
                    if (!string.IsNullOrWhiteSpace(labels))
                    {
                        Runner.LogDebug("All labels for {Id}: {Labels}", id, labels);
                    }
                }

                containers.Add(new ContainerProcessInfo(
                    Platform: "docker",
                    Id: id,
                    Name: names,
                    Image: image,
                    Status: status,
                    State: state,
                    Ports: ports,
                    Project: project,
                    HostPlatform: "win"));
            }
            catch (Exception ex)
            {
                Runner.LogWarning(ex, "Failed to parse docker ps line.");
            }
        }

        return containers;
    }

    public override async Task<IReadOnlyList<ImageInfo>> GetImagesDetailedAsync(CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "images", "--no-trunc", "--format", "json" };
        var result = await ExecAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<ImageInfo>();

        var list = new List<ImageInfo>();
        foreach (var root in EnumerateJsonElements(result.StandardOutput))
        {
            try
            {
                list.Add(new ImageInfo(
                    Platform: "docker",
                    Name: root.GetPropertyOrDefault("Repository"),
                    Tag: root.GetPropertyOrDefault("Tag"),
                    Id: root.GetPropertyOrDefault("ID"),
                    Size: root.GetPropertyOrDefault("Size"),
                    HostPlatform: "win"));
            }
            catch (Exception ex)
            {
                Runner.LogWarning(ex, "Failed to parse docker image line.");
            }
        }

        return list;
    }

    public override async Task<IReadOnlyList<VolumeInfo>> GetVolumesDetailedAsync(CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "volume", "ls", "--format", "json" };
        var result = await ExecAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<VolumeInfo>();

        var list = new List<VolumeInfo>();
        foreach (var root in EnumerateJsonElements(result.StandardOutput))
        {
            try
            {
                list.Add(new VolumeInfo(
                    Platform: "docker",
                    Name: root.GetPropertyOrDefault("Name"),
                    Size: string.Empty,
                    HostPlatform: "win"));
            }
            catch (Exception ex)
            {
                Runner.LogWarning(ex, "Failed to parse docker volume line.");
            }
        }

        return list;
    }

    private static IEnumerable<JsonElement> EnumerateJsonElements(string output)
    {
        var trimmed = output.Trim();
        if (string.IsNullOrEmpty(trimmed)) yield break;

        if (trimmed.StartsWith("["))
        {
            var doc = JsonDocument.Parse(trimmed);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                yield return item;
            }
        }
        else
        {
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var doc = JsonDocument.Parse(line);
                yield return doc.RootElement;
            }
        }
    }
}
