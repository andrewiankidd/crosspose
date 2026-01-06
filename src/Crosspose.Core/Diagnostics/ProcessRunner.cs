using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Crosspose.Core.Diagnostics;

/// <summary>
/// Lightweight process runner used by the CLI and conversion pipelines to shell out to tools
/// such as helm, docker, or podman while capturing output for logging and diagnostics.
/// </summary>
public sealed class ProcessRunner
{
    private readonly ILogger _logger;

    internal ILogger Logger => _logger;

    internal void LogInformation(string message, params object?[] args) => _logger.LogInformation(message, args);
    internal void LogDebug(string message, params object?[] args) => _logger.LogDebug(message, args);
    internal void LogWarning(string message, params object?[] args) => _logger.LogWarning(message, args);
    internal void LogWarning(Exception ex, string message, params object?[] args) => _logger.LogWarning(ex, message, args);

    public ProcessRunner(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Optional handler invoked for each line written to stdout/stderr.
    /// </summary>
    public Action<string>? OutputHandler { get; set; }

    public async Task<ProcessResult> RunAsync(
        string command,
        string arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        try
        {
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var exitCompletion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is { } line)
                {
                    stdOut.AppendLine(line);
                    OutputHandler?.Invoke(line);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is { } line)
                {
                    stdErr.AppendLine(line);
                    OutputHandler?.Invoke(line);
                }
            };

            process.Exited += (_, _) => exitCompletion.TrySetResult(process.ExitCode);

            _logger.LogDebug("Running command: {Command} {Arguments}", command, arguments);

            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    if (value is null) continue;
                    startInfo.Environment[key] = value;
                }
            }

            if (!process.Start())
            {
                throw new InvalidOperationException($"Process '{command}' failed to start.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await using var _ = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cancel process {Command}", command);
                }
            });

            var exitCode = await exitCompletion.Task.ConfigureAwait(false);
            _logger.LogInformation("Command completed: {Command} {Arguments} (exit {ExitCode})", command, arguments, exitCode);
            return new ProcessResult(exitCode, stdOut.ToString().TrimEnd(), stdErr.ToString().TrimEnd());
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            var message = $"Command not found: {command}";
            _logger.LogWarning(ex, message);
            return new ProcessResult(-1, string.Empty, message);
        }
    }
}
