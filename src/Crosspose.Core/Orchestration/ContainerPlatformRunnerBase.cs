using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Orchestration;

public abstract class ContainerPlatformRunnerBase : VirtualizationPlatformRunnerBase, IContainerPlatformRunner
{
    protected ContainerPlatformRunnerBase(string baseCommand, ProcessRunner runner)
        : base(baseCommand, runner)
    {
    }

    public virtual Task<PlatformCommandResult> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var args = includeAll ? new[] { "ps", "-a" } : new[] { "ps" };
        return ExecuteAndWrapAsync(args, cancellationToken);
    }

    public virtual Task<PlatformCommandResult> GetImagesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAndWrapAsync(new[] { "images" }, cancellationToken);

    public virtual Task<PlatformCommandResult> GetVolumesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAndWrapAsync(new[] { "volume", "ls" }, cancellationToken);

    public virtual Task<IReadOnlyList<ContainerProcessInfo>> GetContainersDetailedAsync(bool includeAll = true, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ContainerProcessInfo>>(Array.Empty<ContainerProcessInfo>());

    public virtual Task<IReadOnlyList<ImageInfo>> GetImagesDetailedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImageInfo>>(Array.Empty<ImageInfo>());

    public virtual Task<IReadOnlyList<VolumeInfo>> GetVolumesDetailedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VolumeInfo>>(Array.Empty<VolumeInfo>());

    public virtual async Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "start", id }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    public virtual async Task<bool> StopContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "stop", id }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    public virtual async Task<bool> RemoveContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "rm", "-f", id }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    public virtual async Task<bool> RemoveImageAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "rmi", "-f", id }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    public virtual async Task<bool> RemoveVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "volume", "rm", "-f", name }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    public virtual async Task<bool> PruneImagesAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "image", "prune", "-af" }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    public virtual async Task<bool> PruneVolumesAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "volume", "prune", "-f" }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    public virtual async Task<ContainerInspectResult?> InspectContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "inspect", id }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput)) return null;
        try { return ContainerInspectParser.ParseInspect(result.StandardOutput.Trim()); }
        catch { return null; }
    }

    public virtual async Task<ContainerStatsResult?> GetContainerStatsAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecAsync(new[] { "stats", "--no-stream", "--format", "json", id }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var output = (result.StandardOutput + result.StandardError).Trim();
        if (string.IsNullOrWhiteSpace(output)) return null;
        try { return ContainerInspectParser.ParseStats(output); }
        catch { return null; }
    }

    public virtual async Task<ProcessResult> ExecInContainerAsync(string id, string commandLine, CancellationToken cancellationToken = default)
    {
        var parts = new[] { "exec", id }.Concat(commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return await ExecAsync(parts, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual Task<ProcessResult> GetContainerLogsAsync(string id, int tail = 500, CancellationToken cancellationToken = default) =>
        ExecAsync(new[] { "logs", "--tail", tail.ToString(), id }, cancellationToken: cancellationToken);

    protected virtual async Task<PlatformCommandResult> ExecuteAndWrapAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var result = await ExecAsync(args, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new PlatformCommandResult(BaseCommand, result);
    }
}
