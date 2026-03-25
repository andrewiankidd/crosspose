using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Core.Tests.Orchestration;

public class PodmanContainerRunnerTests
{
    // Podman outputs JSON arrays (not newline-delimited)
    // Labels are JSON objects (not comma-separated strings like Docker)
    // ID is "Id" (PascalCase, not "ID" uppercase)
    // Names can be a JSON array

    private const string PodmanPsJsonArray = """
        [
          {"Id":"pod1","Names":["web"],"Image":"myapp:latest","Status":"Up 2 hours","State":"running","Ports":[{"host_port":"8080","container_port":"80"}],"Labels":{"com.docker.compose.project":"myproject"}},
          {"Id":"pod2","Names":["db"],"Image":"postgres:15","Status":"Exited (0)","State":"exited","Ports":[],"Labels":{}}
        ]
        """;

    private const string PodmanPsEmpty = "[]";

    private const string PodmanPsNamesAsString = """
        [
          {"Id":"pod3","Names":"single-name","Image":"nginx","Status":"Up","State":"running","Ports":[],"Labels":{}}
        ]
        """;

    private const string PodmanImagesJson = """
        [
          {"Id":"sha256:abc123","Names":["docker.io/library/nginx:alpine"],"Repository":"","Tag":"","Size":25000000},
          {"Id":"def456","Repository":"myapp","Tag":"v2","Names":[],"Size":150000000}
        ]
        """;

    private const string PodmanVolumesJson = """
        [
          {"Name":"pgdata","Size":""},
          {"Name":"redis-data","Size":"1024"}
        ]
        """;

    [Fact]
    public async Task GetContainersDetailedAsync_JsonArray_ParsesContainers()
    {
        var runner = CreatePodmanRunner(PodmanPsJsonArray);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Equal(2, containers.Count);
        Assert.Equal("pod1", containers[0].Id);
        Assert.Equal("web", containers[0].Name);
        Assert.Equal("running", containers[0].State);
        Assert.True(containers[0].IsRunning);
        Assert.Equal("myproject", containers[0].Project);
        Assert.Equal("wsl-podman", containers[0].Platform);
        Assert.Equal("lin", containers[0].HostPlatform);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_ExtractsProjectFromLabelsObject()
    {
        var runner = CreatePodmanRunner(PodmanPsJsonArray);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Equal("myproject", containers[0].Project);
        Assert.Null(containers[1].Project); // empty labels object
    }

    [Fact]
    public async Task GetContainersDetailedAsync_NamesAsString_Handled()
    {
        var runner = CreatePodmanRunner(PodmanPsNamesAsString);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Single(containers);
        Assert.Equal("single-name", containers[0].Name);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_EmptyArray_ReturnsEmpty()
    {
        var runner = CreatePodmanRunner(PodmanPsEmpty);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Empty(containers);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_EmptyOutput_ReturnsEmpty()
    {
        var runner = CreatePodmanRunner("");
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Empty(containers);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_MalformedJson_FallsBackToTableParse()
    {
        var tableOutput = """
            CONTAINER ID  IMAGE         COMMAND  CREATED  STATUS     PORTS  NAMES
            abc123def     nginx:latest  nginx    1h ago   Up 1 hour         web-server
            """;
        var runner = CreatePodmanRunner(tableOutput);
        var containers = await runner.GetContainersDetailedAsync();

        // Table fallback should parse at least one row
        Assert.True(containers.Count >= 1);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_PortsArray_FormatsCorrectly()
    {
        var runner = CreatePodmanRunner(PodmanPsJsonArray);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Contains("8080", containers[0].Ports);
        Assert.Contains("80", containers[0].Ports);
    }

    [Fact]
    public async Task GetImagesDetailedAsync_ParsesPodmanImages()
    {
        var runner = CreatePodmanRunner(PodmanImagesJson, forImages: true);
        var images = await runner.GetImagesDetailedAsync();

        Assert.Equal(2, images.Count);
        // First image has no Repository but has Names — should resolve from Names
        Assert.Equal("lin", images[0].HostPlatform);
    }

    [Fact]
    public async Task GetVolumesDetailedAsync_ParsesPodmanVolumes()
    {
        var runner = CreatePodmanRunner(PodmanVolumesJson, forVolumes: true);
        var volumes = await runner.GetVolumesDetailedAsync();

        Assert.Equal(2, volumes.Count);
        Assert.Equal("pgdata", volumes[0].Name);
        Assert.Equal("redis-data", volumes[1].Name);
    }

    private static PodmanContainerRunner CreatePodmanRunner(string stdout, bool forImages = false, bool forVolumes = false)
    {
        var processRunner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        return new PodmanContainerRunnerTestable(processRunner, stdout);
    }
}

file sealed class PodmanContainerRunnerTestable : PodmanContainerRunner
{
    private readonly string _cannedOutput;

    public PodmanContainerRunnerTestable(ProcessRunner runner, string cannedOutput)
        : base(runner, runInsideWsl: true, wslDistribution: "test")
    {
        _cannedOutput = cannedOutput;
    }

    public override Task<ProcessResult> ExecAsync(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProcessResult(0, _cannedOutput, string.Empty));
    }
}
