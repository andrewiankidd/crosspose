using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Core.Tests.Helpers;

/// <summary>
/// Creates a real <see cref="ProcessRunner"/> that invokes the TestShim exe
/// to replay captured output. This exercises the full ProcessRunner pipeline
/// (process spawn, async output capture, exit code) end-to-end.
/// </summary>
public static class ShimRunner
{
    private static readonly Lazy<string> ShimPath = new(() =>
    {
        // Walk up from the test assembly's output directory to find the shim exe.
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "Crosspose.TestShim.exe");
            if (File.Exists(candidate)) return candidate;

            // Check sibling project output
            var shimDir = Path.Combine(dir, "..", "Crosspose.TestShim", "Debug", "net10.0", "Crosspose.TestShim.exe");
            if (File.Exists(shimDir)) return Path.GetFullPath(shimDir);

            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: assume it's on PATH or in the same output directory via project reference
        return "Crosspose.TestShim";
    });

    /// <summary>
    /// Creates a ProcessRunner and returns the command + arguments to invoke the shim
    /// with the specified stdout content written to a temp fixture file.
    /// </summary>
    public static (ProcessRunner Runner, string Command, string Arguments) ForStdout(string stdout, int exitCode = 0)
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, stdout);
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var args = $"--stdout \"{file}\" --exit {exitCode}";
        return (runner, ShimPath.Value, args);
    }

    /// <summary>
    /// Runs the shim and returns the ProcessResult directly.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(string stdout = "", string stderr = "", int exitCode = 0)
    {
        var stdoutFile = Path.GetTempFileName();
        var stderrFile = Path.GetTempFileName();
        File.WriteAllText(stdoutFile, stdout);
        File.WriteAllText(stderrFile, stderr);

        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var args = $"--stdout \"{stdoutFile}\" --stderr \"{stderrFile}\" --exit {exitCode}";
        return await runner.RunAsync(ShimPath.Value, args);
    }
}
