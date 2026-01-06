using System;
using System.Text;
using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Orchestration;

/// <summary>
/// Aggregates results from multiple container platforms (docker + podman).
/// </summary>
public sealed class CombinedContainerPlatformRunner : IContainerPlatformRunner
{
    private readonly IContainerPlatformRunner _docker;
    private readonly IContainerPlatformRunner _podman;

    public CombinedContainerPlatformRunner(IContainerPlatformRunner docker, IContainerPlatformRunner podman)
    {
        _docker = docker;
        _podman = podman;
    }

    public string BaseCommand => "combined";

    public Task<ProcessResult> ExecAsync(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default) =>
        Task.FromException<ProcessResult>(new NotSupportedException("Use specific container methods instead."));

    public async Task<PlatformCommandResult> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var dockerTask = _docker.GetContainersAsync(includeAll, cancellationToken);
        var podmanTask = _podman.GetContainersAsync(includeAll, cancellationToken);
        await Task.WhenAll(dockerTask, podmanTask).ConfigureAwait(false);
        return Merge("containers", dockerTask.Result, podmanTask.Result);
    }

    public async Task<IReadOnlyList<ContainerProcessInfo>> GetContainersDetailedAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var dockerTask = _docker.GetContainersDetailedAsync(includeAll, cancellationToken);
        var podmanTask = _podman.GetContainersDetailedAsync(includeAll, cancellationToken);
        await Task.WhenAll(dockerTask, podmanTask).ConfigureAwait(false);
        return dockerTask.Result
            .Concat(podmanTask.Result)
            .OrderBy(c => NormalizeContainerName(c.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ProjectContainerGroup>> GetContainersGroupedByProjectAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var containers = await GetContainersDetailedAsync(includeAll, cancellationToken).ConfigureAwait(false);
        return containers
            .GroupBy(c => NormalizeProject(c.Project))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProjectContainerGroup(g.Key, g.OrderBy(c => NormalizeContainerName(c.Name), StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<ImageInfo>> GetImagesDetailedAsync(CancellationToken cancellationToken = default)
    {
        var dockerTask = _docker.GetImagesDetailedAsync(cancellationToken);
        var podmanTask = _podman.GetImagesDetailedAsync(cancellationToken);
        await Task.WhenAll(dockerTask, podmanTask).ConfigureAwait(false);
        return dockerTask.Result.Concat(podmanTask.Result).ToList();
    }

    public async Task<IReadOnlyList<VolumeInfo>> GetVolumesDetailedAsync(CancellationToken cancellationToken = default)
    {
        var dockerTask = _docker.GetVolumesDetailedAsync(cancellationToken);
        var podmanTask = _podman.GetVolumesDetailedAsync(cancellationToken);
        await Task.WhenAll(dockerTask, podmanTask).ConfigureAwait(false);
        return dockerTask.Result.Concat(podmanTask.Result).ToList();
    }

    public async Task<PlatformCommandResult> GetImagesAsync(CancellationToken cancellationToken = default)
    {
        var dockerTask = _docker.GetImagesAsync(cancellationToken);
        var podmanTask = _podman.GetImagesAsync(cancellationToken);
        await Task.WhenAll(dockerTask, podmanTask).ConfigureAwait(false);
        return Merge("images", dockerTask.Result, podmanTask.Result);
    }

    public async Task<PlatformCommandResult> GetVolumesAsync(CancellationToken cancellationToken = default)
    {
        var dockerTask = _docker.GetVolumesAsync(cancellationToken);
        var podmanTask = _podman.GetVolumesAsync(cancellationToken);
        await Task.WhenAll(dockerTask, podmanTask).ConfigureAwait(false);
        return Merge("volumes", dockerTask.Result, podmanTask.Result);
    }

    public Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default) =>
        TryResolveRunner(id, out var runner, out var actualId)
            ? runner!.StartContainerAsync(actualId, cancellationToken)
            : Task.FromResult(false);

    public Task<bool> StopContainerAsync(string id, CancellationToken cancellationToken = default) =>
        TryResolveRunner(id, out var runner, out var actualId)
            ? runner!.StopContainerAsync(actualId, cancellationToken)
            : Task.FromResult(false);

    public Task<bool> RemoveContainerAsync(string id, CancellationToken cancellationToken = default) =>
        TryResolveRunner(id, out var runner, out var actualId)
            ? runner!.RemoveContainerAsync(actualId, cancellationToken)
            : Task.FromResult(false);

    public Task<bool> RemoveImageAsync(string id, CancellationToken cancellationToken = default) =>
        TryResolveRunner(id, out var runner, out var actualId)
            ? runner!.RemoveImageAsync(actualId, cancellationToken)
            : Task.FromResult(false);

    public Task<bool> RemoveVolumeAsync(string name, CancellationToken cancellationToken = default) =>
        TryResolveRunner(name, out var runner, out var actualName)
            ? runner!.RemoveVolumeAsync(actualName, cancellationToken)
            : Task.FromResult(false);

    private static PlatformCommandResult Merge(string header, PlatformCommandResult docker, PlatformCommandResult podman)
    {
        var sb = new StringBuilder();
        var stderr = new StringBuilder();

        sb.AppendLine($"=== {header} (docker) ===");
        sb.AppendLine(string.IsNullOrWhiteSpace(docker.Result.StandardOutput) ? "(no output)" : docker.Result.StandardOutput.TrimEnd());
        if (!docker.Result.IsSuccess && !string.IsNullOrWhiteSpace(docker.Result.StandardError))
        {
            stderr.AppendLine("Docker error:");
            stderr.AppendLine(docker.Result.StandardError.TrimEnd());
        }

        sb.AppendLine();
        sb.AppendLine($"=== {header} (podman) ===");
        sb.AppendLine(string.IsNullOrWhiteSpace(podman.Result.StandardOutput) ? "(no output)" : podman.Result.StandardOutput.TrimEnd());
        var podmanErrorText = podman.Result.StandardError?.TrimEnd() ?? string.Empty;
        if (!podman.Result.IsSuccess && !string.IsNullOrWhiteSpace(podmanErrorText))
        {
            if (stderr.Length > 0) stderr.AppendLine();
            stderr.AppendLine("Podman error:");
            stderr.AppendLine(podmanErrorText);
        }

        var anyError = !docker.Result.IsSuccess || !podman.Result.IsSuccess;
        var exitCode = anyError
            ? (docker.Result.IsSuccess ? podman.Result.ExitCode : docker.Result.ExitCode != 0 ? docker.Result.ExitCode : 1)
            : 0;

        var merged = new ProcessResult(exitCode, sb.ToString().TrimEnd(), stderr.ToString().TrimEnd());
        return new PlatformCommandResult("combined", merged);
    }

    private bool TryResolveRunner(string qualifiedId, out IContainerPlatformRunner? runner, out string actualId)
    {
        runner = null;
        actualId = string.Empty;
        if (string.IsNullOrWhiteSpace(qualifiedId)) return false;

        var parts = qualifiedId.Split(':', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        var platform = parts[0];
        actualId = parts[1];
        if (string.Equals(platform, "docker", StringComparison.OrdinalIgnoreCase))
        {
            runner = _docker;
            return true;
        }

        if (platform.Contains("podman", StringComparison.OrdinalIgnoreCase))
        {
            runner = _podman;
            return true;
        }

        return false;
    }

    private static string NormalizeProject(string? project) =>
        string.IsNullOrWhiteSpace(project) ? string.Empty : project;

    private static string NormalizeContainerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return name.Replace('_', '-');
    }

    public sealed record ProjectContainerGroup(string Project, IReadOnlyList<ContainerProcessInfo> Containers);
}
