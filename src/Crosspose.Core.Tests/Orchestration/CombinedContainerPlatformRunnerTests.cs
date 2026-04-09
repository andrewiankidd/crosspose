using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Core.Tests.Orchestration;

public class CombinedContainerPlatformRunnerTests
{
    [Fact]
    public async Task GetContainersDetailedAsync_MergesDockerAndPodman()
    {
        var docker = new FakeContainerRunner("docker", new[]
        {
            new ContainerProcessInfo("docker", "d1", "web", "img:1", "Up", "running", "", "proj", "win")
        });
        var podman = new FakeContainerRunner("podman", new[]
        {
            new ContainerProcessInfo("podman", "p1", "api", "img:2", "Up", "running", "", "proj", "lin")
        });

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        var containers = await combined.GetContainersDetailedAsync();

        Assert.Equal(2, containers.Count);
        Assert.Contains(containers, c => c.Platform == "docker");
        Assert.Contains(containers, c => c.Platform == "podman");
    }

    [Fact]
    public async Task GetContainersDetailedAsync_EmptyDocker_ReturnsOnlyPodman()
    {
        var docker = new FakeContainerRunner("docker", Array.Empty<ContainerProcessInfo>());
        var podman = new FakeContainerRunner("podman", new[]
        {
            new ContainerProcessInfo("podman", "p1", "svc", "img", "Up", "running", "", null, "lin")
        });

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        var containers = await combined.GetContainersDetailedAsync();

        Assert.Single(containers);
        Assert.Equal("podman", containers[0].Platform);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_BothEmpty_ReturnsEmpty()
    {
        var docker = new FakeContainerRunner("docker", Array.Empty<ContainerProcessInfo>());
        var podman = new FakeContainerRunner("podman", Array.Empty<ContainerProcessInfo>());

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        var containers = await combined.GetContainersDetailedAsync();

        Assert.Empty(containers);
    }

    [Fact]
    public async Task GetContainersGroupedByProjectAsync_GroupsByProject()
    {
        var docker = new FakeContainerRunner("docker", new[]
        {
            new ContainerProcessInfo("docker", "d1", "web", "img", "Up", "running", "", "alpha", "win"),
            new ContainerProcessInfo("docker", "d2", "db", "img", "Up", "running", "", "alpha", "win"),
            new ContainerProcessInfo("docker", "d3", "svc", "img", "Up", "running", "", "beta", "win")
        });
        var podman = new FakeContainerRunner("podman", Array.Empty<ContainerProcessInfo>());

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        var groups = await combined.GetContainersGroupedByProjectAsync();

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Project == "alpha" && g.Containers.Count == 2);
        Assert.Contains(groups, g => g.Project == "beta" && g.Containers.Count == 1);
    }

    [Fact]
    public async Task StartContainerAsync_RoutesToDockerRunner()
    {
        var docker = new FakeContainerRunner("docker", Array.Empty<ContainerProcessInfo>());
        var podman = new FakeContainerRunner("podman", Array.Empty<ContainerProcessInfo>());

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        var result = await combined.StartContainerAsync("docker:abc123");

        Assert.True(result);
        Assert.Equal("abc123", docker.LastStartedId);
    }

    [Fact]
    public async Task StartContainerAsync_RoutesToPodmanRunner()
    {
        var docker = new FakeContainerRunner("docker", Array.Empty<ContainerProcessInfo>());
        var podman = new FakeContainerRunner("podman", Array.Empty<ContainerProcessInfo>());

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        var result = await combined.StartContainerAsync("wsl-podman:xyz789");

        Assert.True(result);
        Assert.Equal("xyz789", podman.LastStartedId);
    }

    [Fact]
    public async Task StartContainerAsync_UnknownPlatform_ReturnsFalse()
    {
        var docker = new FakeContainerRunner("docker", Array.Empty<ContainerProcessInfo>());
        var podman = new FakeContainerRunner("podman", Array.Empty<ContainerProcessInfo>());

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        var result = await combined.StartContainerAsync("unknown:abc");

        Assert.False(result);
    }

    [Fact]
    public async Task StartContainerAsync_InvalidFormat_ReturnsFalse()
    {
        var docker = new FakeContainerRunner("docker", Array.Empty<ContainerProcessInfo>());
        var podman = new FakeContainerRunner("podman", Array.Empty<ContainerProcessInfo>());

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        var result = await combined.StartContainerAsync("nocolon");

        Assert.False(result);
    }

    [Fact]
    public async Task ExecAsync_ThrowsNotSupportedException()
    {
        var docker = new FakeContainerRunner("docker", Array.Empty<ContainerProcessInfo>());
        var podman = new FakeContainerRunner("podman", Array.Empty<ContainerProcessInfo>());

        var combined = new CombinedContainerPlatformRunner(docker, podman);
        await Assert.ThrowsAsync<NotSupportedException>(() => combined.ExecAsync(new[] { "ps" }));
    }
}

file sealed class FakeContainerRunner : IContainerPlatformRunner
{
    private readonly IReadOnlyList<ContainerProcessInfo> _containers;
    public string? LastStartedId { get; private set; }
    public string? LastStoppedId { get; private set; }

    public FakeContainerRunner(string baseCommand, IReadOnlyList<ContainerProcessInfo> containers)
    {
        BaseCommand = baseCommand;
        _containers = containers;
    }

    public string BaseCommand { get; }

    public Task<ProcessResult> ExecAsync(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ProcessResult(0, "", ""));

    public Task<PlatformCommandResult> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default)
        => Task.FromResult(new PlatformCommandResult(BaseCommand, new ProcessResult(0, "", "")));

    public Task<PlatformCommandResult> GetImagesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PlatformCommandResult(BaseCommand, new ProcessResult(0, "", "")));

    public Task<PlatformCommandResult> GetVolumesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PlatformCommandResult(BaseCommand, new ProcessResult(0, "", "")));

    public Task<IReadOnlyList<ContainerProcessInfo>> GetContainersDetailedAsync(bool includeAll = true, CancellationToken cancellationToken = default)
        => Task.FromResult(_containers);

    public Task<IReadOnlyList<ImageInfo>> GetImagesDetailedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ImageInfo>>(Array.Empty<ImageInfo>());

    public Task<IReadOnlyList<VolumeInfo>> GetVolumesDetailedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<VolumeInfo>>(Array.Empty<VolumeInfo>());

    public Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        LastStartedId = id;
        return Task.FromResult(true);
    }

    public Task<bool> StopContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        LastStoppedId = id;
        return Task.FromResult(true);
    }

    public Task<bool> RemoveContainerAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> LoginAsync(string registry, string username, string password, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> RemoveImageAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> RemoveVolumeAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> PruneImagesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> PruneVolumesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<ContainerInspectResult?> InspectContainerAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult<ContainerInspectResult?>(null);

    public Task<ContainerStatsResult?> GetContainerStatsAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult<ContainerStatsResult?>(null);

    public Task<ProcessResult> ExecInContainerAsync(string id, string commandLine, CancellationToken cancellationToken = default)
        => Task.FromResult(new ProcessResult(-1, string.Empty, "Not implemented."));

    public Task<ProcessResult> GetContainerLogsAsync(string id, int tail = 500, bool timestamps = false, CancellationToken cancellationToken = default)
        => Task.FromResult(new ProcessResult(-1, string.Empty, "Not implemented."));
}
