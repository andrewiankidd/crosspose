namespace Crosspose.Core.Orchestration;

public interface IContainerProcess
{
    string Platform { get; }
    string Id { get; }
    string Name { get; }
    string Image { get; }
    string Status { get; }
    string State { get; }
    string Ports { get; }
    string? Project { get; }
    string HostPlatform { get; }
    bool IsRunning { get; }
    /// <summary>
    /// Health status from the container runtime: "healthy", "unhealthy", "starting", or null if
    /// the container has no healthcheck or the runtime does not report it.
    /// </summary>
    string? Health { get; }
}

public sealed record ContainerProcessInfo(
    string Platform,
    string Id,
    string Name,
    string Image,
    string Status,
    string State,
    string Ports,
    string? Project,
    string HostPlatform,
    string? Health = null) : IContainerProcess
{
    public bool IsRunning => string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the health token — "healthy", "unhealthy", or "starting" — from a Status string
    /// like "Up 5 minutes (healthy)". Returns null if absent.
    /// </summary>
    public static string? ParseHealth(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var open = status.LastIndexOf('(');
        var close = status.LastIndexOf(')');
        if (open < 0 || close <= open) return null;
        var token = status[(open + 1)..close].Trim().ToLowerInvariant();
        return token is "healthy" or "unhealthy" or "starting" ? token : null;
    }
}

public interface IImageInfo
{
    string Platform { get; }
    string Name { get; }
    string Tag { get; }
    string Id { get; }
    string Size { get; }
    string HostPlatform { get; }
}

public sealed record ImageInfo(
    string Platform,
    string Name,
    string Tag,
    string Id,
    string Size,
    string HostPlatform) : IImageInfo;

public interface IVolumeInfo
{
    string Platform { get; }
    string Name { get; }
    string Size { get; }
    string HostPlatform { get; }
}

public sealed record VolumeInfo(
    string Platform,
    string Name,
    string Size,
    string HostPlatform) : IVolumeInfo;
