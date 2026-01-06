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
    string HostPlatform) : IContainerProcess
{
    public bool IsRunning => string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);
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
