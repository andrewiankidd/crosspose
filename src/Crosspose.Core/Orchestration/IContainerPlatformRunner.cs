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
    Task<bool> RemoveImageAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> RemoveVolumeAsync(string name, CancellationToken cancellationToken = default);
}
