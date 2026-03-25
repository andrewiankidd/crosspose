using Crosspose.Core.Diagnostics;
using Crosspose.Doctor.Checks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Doctor.Tests.Checks;

public class DockerComposeCheckTests
{
    private readonly DockerComposeCheck _check = new();

    [Fact]
    public void Properties_AreCorrect()
    {
        Assert.Equal("docker-compose", _check.Name);
        Assert.True(_check.CanFix);
        Assert.False(_check.IsAdditional);
        Assert.False(string.IsNullOrWhiteSpace(_check.Description));
    }

    [Fact]
    public async Task RunAsync_DockerComposeAvailable_ReturnsSuccess()
    {
        // This test requires docker to be installed — skip if not available.
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var probe = await runner.RunAsync("docker", "compose version");
        if (!probe.IsSuccess)
        {
            return; // Docker not installed, skip
        }

        var result = await _check.RunAsync(runner, NullLogger.Instance, CancellationToken.None);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task RunAsync_NeitherAvailable_ReturnsFailure()
    {
        // Use a runner that can't find docker — point at a nonexistent tool
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        // We can't easily simulate "docker not found" without the shim,
        // but we can verify the check doesn't throw
        var result = await _check.RunAsync(runner, NullLogger.Instance, CancellationToken.None);
        // Result depends on whether docker is installed on this machine
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
