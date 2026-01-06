using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Crosspose.Core.Orchestration;

public sealed class PodmanContainerRunner : ContainerPlatformRunnerBase
{
    public PodmanContainerRunner(ProcessRunner runner, bool runInsideWsl = true, string? wslDistribution = null)
        : base(runInsideWsl ? "wsl" : "podman", runner)
    {
        _runInsideWsl = runInsideWsl;
        _wslDistribution = runInsideWsl
            ? (string.IsNullOrWhiteSpace(wslDistribution) ? CrossposeEnvironment.WslDistro : wslDistribution)
            : null;
    }

    private readonly bool _runInsideWsl;
    private readonly string? _wslDistribution;

    public override Task<ProcessResult> ExecAsync(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
    {
        var argList = args.ToList();

        if (!_runInsideWsl)
        {
            var argumentString = string.Join(" ", argList);
            return Runner.RunAsync(BaseCommand, argumentString, environment: environment, cancellationToken: cancellationToken);
        }

        var builder = new List<string>();
        if (!string.IsNullOrWhiteSpace(_wslDistribution))
        {
            builder.Add("--distribution");
            builder.Add(_wslDistribution!);
        }
        builder.Add("--exec");

        var requiresSudo = argList.Count > 0 && string.Equals(argList[0], "compose", StringComparison.OrdinalIgnoreCase);
        if (requiresSudo)
        {
            builder.Add("sudo");
        }

        builder.Add("podman");
        builder.AddRange(argList);

        var argumentStringWsl = string.Join(" ", builder);
        return Runner.RunAsync("wsl", argumentStringWsl, environment: environment, cancellationToken: cancellationToken);
    }

    protected override async Task<PlatformCommandResult> ExecuteAndWrapAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var result = await ExecAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
        var platformName = _runInsideWsl ? "wsl-podman" : "podman";
        return new PlatformCommandResult(platformName, result);
    }

    public override Task<PlatformCommandResult> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "ps", "--format", "json" };
        if (includeAll)
        {
            args.Insert(1, "-a");
        }

        return ExecuteAndWrapAsync(args, cancellationToken);
    }

    public override async Task<IReadOnlyList<ContainerProcessInfo>> GetContainersDetailedAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "ps", "--format", "json" };
        if (includeAll)
        {
            args.Insert(1, "-a");
        }

        // For podman-in-wsl we prefix "podman" after the base command.
        var result = await ExecAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<ContainerProcessInfo>();
        }

        var containers = new List<ContainerProcessInfo>();
        try
        {
            var doc = JsonDocument.Parse(result.StandardOutput);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = item.GetPropertyOrDefault("Id");
                var names = ExtractNames(item);
                var image = item.GetPropertyOrDefault("Image");
                var status = item.GetPropertyOrDefault("Status");
                var state = item.GetPropertyOrDefault("State");
                var ports = FormatPorts(item);
                string? project = null;
                if (item.TryGetProperty("Labels", out var labelsElement) && labelsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var label in labelsElement.EnumerateObject())
                    {
                        if (label.NameEquals("com.docker.compose.project"))
                        {
                            project = label.Value.GetString();
                            break;
                        }
                    }
                }

                containers.Add(new ContainerProcessInfo(
                    Platform: _runInsideWsl ? "wsl-podman" : "podman",
                    Id: id,
                    Name: names,
                    Image: image,
                    Status: status,
                    State: state,
                    Ports: ports,
                    Project: project,
                    HostPlatform: "lin"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Podman parse failed: {ex}");
        }

        if (containers.Count > 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return containers;
        }

        return TableParseFallback(result.StandardOutput, _runInsideWsl ? "wsl-podman" : "podman");
    }

    public override async Task<IReadOnlyList<ImageInfo>> GetImagesDetailedAsync(CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "images", "--format", "json" };
        var result = await ExecAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<ImageInfo>();

        var list = new List<ImageInfo>();
        try
        {
            var doc = JsonDocument.Parse(result.StandardOutput);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var (name, tag) = ResolveImageReference(item);
                var normalizedId = NormalizeImageId(item.GetPropertyOrDefault("Id"));
                var normalizedSize = NormalizeImageSize(item.GetPropertyOrDefault("Size"));
                list.Add(new ImageInfo(
                    Platform: _runInsideWsl ? "wsl-podman" : "podman",
                    Name: name,
                    Tag: tag,
                    Id: normalizedId,
                    Size: normalizedSize,
                    HostPlatform: "lin"));
            }
        }
        catch (Exception ex)
        {
            Runner.LogWarning(ex, "Failed to parse podman images output: {Output}", result.StandardOutput);
        }

        return list;
    }

    public override async Task<IReadOnlyList<VolumeInfo>> GetVolumesDetailedAsync(CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "volume", "ls", "--format", "json" };
        var result = await ExecAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<VolumeInfo>();

        var list = new List<VolumeInfo>();
        try
        {
            var doc = JsonDocument.Parse(result.StandardOutput);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                list.Add(new VolumeInfo(
                    Platform: _runInsideWsl ? "wsl-podman" : "podman",
                    Name: item.GetPropertyOrDefault("Name"),
                    Size: item.GetPropertyOrDefault("Size"), // may be empty
                    HostPlatform: "lin"));
            }
        }
        catch (Exception ex)
        {
            Runner.LogWarning(ex, "Failed to parse podman volumes output: {Output}", result.StandardOutput);
        }

        return list;
    }

    private static IReadOnlyList<ContainerProcessInfo> TableParseFallback(string stdOut, string platform)
    {
        var list = new List<ContainerProcessInfo>();
        var lines = stdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) return list;

        for (int i = 1; i < lines.Length; i++)
        {
            var parts = Regex.Split(lines[i].Trim(), "\\s{2,}").Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (parts.Length < 5) continue;

            var id = parts.ElementAtOrDefault(0) ?? string.Empty;
            var image = parts.ElementAtOrDefault(1) ?? string.Empty;
            var statusFull = parts.ElementAtOrDefault(4) ?? string.Empty;
            var state = string.IsNullOrWhiteSpace(statusFull) ? string.Empty : statusFull.Split(' ')[0];
            var ports = parts.ElementAtOrDefault(5) ?? string.Empty;
            var name = parts.LastOrDefault() ?? string.Empty;

            list.Add(new ContainerProcessInfo(
                Platform: platform,
                Id: id,
                Name: name,
                Image: image,
                Status: statusFull,
                State: state,
                Ports: ports,
                Project: null,
                HostPlatform: "lin"));
        }

        return list;
    }

    private static string ExtractNames(JsonElement item)
    {
        if (!item.TryGetProperty("Names", out var namesElement))
        {
            return string.Empty;
        }

        return namesElement.ValueKind switch
        {
            JsonValueKind.Array => string.Join(", ",
                namesElement.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Select(s => s?.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            JsonValueKind.String => namesElement.GetString()?.Trim() ?? string.Empty,
            _ => namesElement.ToString()
        };
    }

    private static string FormatPorts(JsonElement item)
    {
        if (!item.TryGetProperty("Ports", out var portsElement))
        {
            return string.Empty;
        }

        if (portsElement.ValueKind == JsonValueKind.Array)
        {
            var formatted = new List<string>();
            foreach (var port in portsElement.EnumerateArray())
            {
                var containerPort = port.GetPropertyOrDefault("container_port");
                var hostPort = port.GetPropertyOrDefault("host_port");
                if (string.IsNullOrWhiteSpace(containerPort) && string.IsNullOrWhiteSpace(hostPort))
                {
                    continue;
                }

                var host = string.IsNullOrWhiteSpace(hostPort) ? "*" : hostPort;
                var container = string.IsNullOrWhiteSpace(containerPort) ? string.Empty : containerPort;
                var entry = string.IsNullOrWhiteSpace(container) ? host : $"{host}:{container}";
                formatted.Add(entry);
            }

            return formatted.Count > 0 ? string.Join(", ", formatted) : string.Empty;
        }

        return portsElement.ValueKind == JsonValueKind.Object
            ? portsElement.ToString()
            : portsElement.GetString() ?? string.Empty;
    }

    private static (string name, string tag) ResolveImageReference(JsonElement item)
    {
        var repository = item.GetPropertyOrDefault("Repository");
        var tag = item.GetPropertyOrDefault("Tag");
        if (!string.IsNullOrWhiteSpace(repository))
        {
            return (repository, string.IsNullOrWhiteSpace(tag) ? "latest" : tag);
        }

        var reference = ExtractFirstName(item);
        if (!string.IsNullOrWhiteSpace(reference))
        {
            return SplitImageReference(reference!);
        }

        return ("<none>", "<none>");
    }

    private static string? ExtractFirstName(JsonElement item)
    {
        if (!item.TryGetProperty("Names", out var namesElement))
        {
            return null;
        }

        if (namesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in namesElement.EnumerateArray())
            {
                var value = entry.ValueKind == JsonValueKind.String ? entry.GetString() : entry.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return null;
        }

        if (namesElement.ValueKind == JsonValueKind.String)
        {
            var value = namesElement.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return namesElement.ToString();
    }

    private static (string name, string tag) SplitImageReference(string reference)
    {
        var trimmed = reference.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return ("<none>", "<none>");

        var lastSlash = trimmed.LastIndexOf('/');
        var lastColon = trimmed.LastIndexOf(':');
        if (lastColon > lastSlash)
        {
            var name = trimmed[..lastColon];
            var tag = trimmed[(lastColon + 1)..];
            return (name, string.IsNullOrWhiteSpace(tag) ? "latest" : tag);
        }

        return (trimmed, "latest");
    }

    private static string NormalizeImageId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        var trimmed = id.Trim();
        return trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"sha256:{trimmed}";
    }

    private static string NormalizeImageSize(string? rawSize)
    {
        if (string.IsNullOrWhiteSpace(rawSize)) return string.Empty;
        var sanitized = rawSize.Trim();
        if (double.TryParse(sanitized, NumberStyles.Float, CultureInfo.InvariantCulture, out var bytes))
        {
            var gb = bytes / (1024d * 1024d * 1024d);
            if (gb >= 0.1)
            {
                return $"{gb:0.##}GB";
            }

            var mb = bytes / (1024d * 1024d);
            if (mb >= 0.1)
            {
                return $"{mb:0.##}MB";
            }

            var kb = bytes / 1024d;
            return $"{kb:0.##}KB";
        }

        return sanitized;
    }
}
