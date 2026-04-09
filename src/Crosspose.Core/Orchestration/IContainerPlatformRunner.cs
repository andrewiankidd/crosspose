using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Orchestration;

public interface IContainerPlatformRunner : IVirtualizationPlatformRunner
{
    Task<PlatformCommandResult> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default);
    Task<PlatformCommandResult> GetImagesAsync(CancellationToken cancellationToken = default);
    Task<PlatformCommandResult> GetVolumesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContainerProcessInfo>> GetContainersDetailedAsync(bool includeAll = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImageInfo>> GetImagesDetailedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VolumeInfo>> GetVolumesDetailedAsync(CancellationToken cancellationToken = default);
    Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> StopContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> RemoveContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> LoginAsync(string registry, string username, string password, CancellationToken cancellationToken = default);
    Task<bool> RemoveImageAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> RemoveVolumeAsync(string name, CancellationToken cancellationToken = default);
    /// <summary>Removes all images not referenced by any container (docker/podman image prune -af).</summary>
    Task<bool> PruneImagesAsync(CancellationToken cancellationToken = default);
    /// <summary>Removes all volumes not referenced by any container (docker/podman volume prune -f).</summary>
    Task<bool> PruneVolumesAsync(CancellationToken cancellationToken = default);
    Task<ContainerInspectResult?> InspectContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<ContainerStatsResult?> GetContainerStatsAsync(string id, CancellationToken cancellationToken = default);
    Task<ProcessResult> ExecInContainerAsync(string id, string commandLine, CancellationToken cancellationToken = default);
    Task<ProcessResult> GetContainerLogsAsync(string id, int tail = 500, bool timestamps = false, CancellationToken cancellationToken = default);
}
