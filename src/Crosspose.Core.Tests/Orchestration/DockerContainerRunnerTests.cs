using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Core.Tests.Orchestration;

public class DockerContainerRunnerTests
{
    private static ProcessRunner NullRunner() => new(NullLogger<ProcessRunner>.Instance);

    // Sample docker ps --format json output (newline-delimited objects)
    private const string DockerPsNewlineDelimited = """
        {"ID":"abc123","Names":"web-api","Image":"myapp:latest","Status":"Up 2 hours","State":"running","Ports":"0.0.0.0:8080->80/tcp","Labels":"com.docker.compose.project=myproject,com.docker.compose.service=web"}
        {"ID":"def456","Names":"db","Image":"mssql:2022","Status":"Exited (0) 1 hour ago","State":"exited","Ports":"","Labels":"com.docker.compose.project=myproject,com.docker.compose.service=db"}
        """;

    // Same data as JSON array
    private const string DockerPsJsonArray = """
        [
          {"ID":"abc123","Names":"web-api","Image":"myapp:latest","Status":"Up 2 hours","State":"running","Ports":"0.0.0.0:8080->80/tcp","Labels":"com.docker.compose.project=myproject,com.docker.compose.service=web"},
          {"ID":"def456","Names":"db","Image":"mssql:2022","Status":"Exited (0) 1 hour ago","State":"exited","Ports":"","Labels":"com.docker.compose.project=myproject,com.docker.compose.service=db"}
        ]
        """;

    // Container with no compose labels
    private const string DockerPsNoLabels = """
        {"ID":"ghi789","Names":"standalone","Image":"nginx:alpine","Status":"Up 5 min","State":"running","Ports":"443/tcp","Labels":""}
        """;

    [Fact]
    public async Task GetContainersDetailedAsync_NewlineDelimitedJson_ParsesAllContainers()
    {
        var runner = CreateRunnerWithCannedOutput(DockerPsNewlineDelimited);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Equal(2, containers.Count);
        Assert.Equal("abc123", containers[0].Id);
        Assert.Equal("web-api", containers[0].Name);
        Assert.Equal("running", containers[0].State);
        Assert.True(containers[0].IsRunning);
        Assert.Equal("myproject", containers[0].Project);
        Assert.Equal("docker", containers[0].Platform);
        Assert.Equal("win", containers[0].HostPlatform);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_JsonArray_ParsesAllContainers()
    {
        var runner = CreateRunnerWithCannedOutput(DockerPsJsonArray);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Equal(2, containers.Count);
        Assert.Equal("def456", containers[1].Id);
        Assert.Equal("exited", containers[1].State);
        Assert.False(containers[1].IsRunning);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_NoLabels_ProjectIsNull()
    {
        var runner = CreateRunnerWithCannedOutput(DockerPsNoLabels);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Single(containers);
        Assert.Null(containers[0].Project);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_EmptyOutput_ReturnsEmptyList()
    {
        var runner = CreateRunnerWithCannedOutput("");
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Empty(containers);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_MalformedJson_ReturnsPartialResults()
    {
        var output = """
            {"ID":"good1","Names":"ok","Image":"img","Status":"Up","State":"running","Ports":"","Labels":""}
            not valid json
            {"ID":"good2","Names":"ok2","Image":"img2","Status":"Up","State":"running","Ports":"","Labels":""}
            """;
        var runner = CreateRunnerWithCannedOutput(output);
        var containers = await runner.GetContainersDetailedAsync();

        // Should parse at least the valid lines, skipping the bad one
        Assert.True(containers.Count >= 1);
    }

    [Fact]
    public async Task GetContainersDetailedAsync_ExtractsComposeProject_FromMultipleLabels()
    {
        var output = """
            {"ID":"x","Names":"svc","Image":"img","Status":"Up","State":"running","Ports":"","Labels":"env=dev,com.docker.compose.project=my-app,tier=backend"}
            """;
        var runner = CreateRunnerWithCannedOutput(output);
        var containers = await runner.GetContainersDetailedAsync();

        Assert.Single(containers);
        Assert.Equal("my-app", containers[0].Project);
    }

    [Fact]
    public async Task GetImagesDetailedAsync_ParsesDockerImages()
    {
        var output = """
            {"Repository":"myapp","Tag":"latest","ID":"sha256:abc","Size":"150MB"}
            {"Repository":"nginx","Tag":"alpine","ID":"sha256:def","Size":"23MB"}
            """;
        var runner = CreateRunnerWithCannedOutput(output);
        var images = await runner.GetImagesDetailedAsync();

        Assert.Equal(2, images.Count);
        Assert.Equal("myapp", images[0].Name);
        Assert.Equal("latest", images[0].Tag);
        Assert.Equal("docker", images[0].Platform);
        Assert.Equal("win", images[0].HostPlatform);
    }

    [Fact]
    public async Task GetVolumesDetailedAsync_ParsesDockerVolumes()
    {
        var output = """
            {"Name":"vol1"}
            {"Name":"vol2"}
            """;
        var runner = CreateRunnerWithCannedOutput(output);
        var volumes = await runner.GetVolumesDetailedAsync();

        Assert.Equal(2, volumes.Count);
        Assert.Equal("vol1", volumes[0].Name);
    }

    /// <summary>
    /// Creates a DockerContainerRunner backed by a ProcessRunner that returns canned output
    /// for any command. Uses cmd /c type to replay the fixture.
    /// </summary>
    private static DockerContainerRunner CreateRunnerWithCannedOutput(string stdout, int exitCode = 0)
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, stdout);
        var processRunner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        // We can't directly inject output, so we use a wrapper approach:
        // Create a derived test helper or use real parsing tests with known fixtures.
        // For now, test the JSON parsing indirectly via the full runner.
        return new DockerContainerRunnerTestable(processRunner, file, exitCode);
    }
}

/// <summary>
/// Test-only subclass that overrides ExecAsync to return canned output from a file.
/// This is necessary because ProcessRunner is sealed and DockerContainerRunner calls
/// ExecAsync internally.
/// </summary>
file sealed class DockerContainerRunnerTestable : DockerContainerRunner
{
    private readonly string _outputFile;
    private readonly int _exitCode;

    public DockerContainerRunnerTestable(ProcessRunner runner, string outputFile, int exitCode)
        : base(runner)
    {
        _outputFile = outputFile;
        _exitCode = exitCode;
    }

    public override Task<ProcessResult> ExecAsync(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
    {
        var output = File.ReadAllText(_outputFile);
        return Task.FromResult(new ProcessResult(_exitCode, output, string.Empty));
    }
}
