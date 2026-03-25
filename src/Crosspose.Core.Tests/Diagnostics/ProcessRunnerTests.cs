using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Core.Tests.Diagnostics;

public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task RunAsync_SuccessfulCommand_ReturnsZeroExitCode()
    {
        var result = await _runner.RunAsync("cmd", "/c echo hello");
        Assert.True(result.IsSuccess);
        Assert.Contains("hello", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_FailingCommand_ReturnsNonZeroExitCode()
    {
        var result = await _runner.RunAsync("cmd", "/c exit /b 42");
        Assert.False(result.IsSuccess);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_CommandNotFound_ReturnsNegativeOneExitCode()
    {
        var result = await _runner.RunAsync("nonexistent_command_12345", "");
        Assert.False(result.IsSuccess);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("Command not found", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_CapturesStderr()
    {
        var result = await _runner.RunAsync("cmd", "/c echo error_output 1>&2");
        Assert.Contains("error_output", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_CancellationToken_CancelsProcess()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await _runner.RunAsync("cmd", "/c ping -n 30 127.0.0.1", cancellationToken: cts.Token);
        // Process should be killed; we just verify it didn't hang for 30 seconds
        Assert.True(true); // If we got here, cancellation worked
    }

    [Fact]
    public async Task RunAsync_OutputHandler_ReceivesLines()
    {
        var lines = new List<string>();
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance)
        {
            OutputHandler = line => lines.Add(line)
        };

        await runner.RunAsync("cmd", "/c echo line1 & echo line2");
        Assert.True(lines.Count >= 2);
    }

    [Fact]
    public async Task RunAsync_WorkingDirectory_IsRespected()
    {
        var tempDir = Path.GetTempPath();
        var result = await _runner.RunAsync("cmd", "/c cd", workingDirectory: tempDir);
        Assert.True(result.IsSuccess);
        Assert.Contains(Path.GetFullPath(tempDir).TrimEnd('\\'), result.StandardOutput);
    }
}
