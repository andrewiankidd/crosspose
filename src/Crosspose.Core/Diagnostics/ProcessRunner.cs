using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

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
        string? stdin = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardInput = stdin is not null,
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

            // When running elevated, the process PATH omits user-level entries (stored in
            // HKCU\Environment) because UAC elevation only inherits the system PATH.
            // Merge them in so tools installed to user scope (e.g. winget, Scoop) are found.
            startInfo.Environment["PATH"] = GetAugmentedPath();

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

            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
                process.StandardInput.Close();
            }

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
                finally
                {
                    // Whether or not Kill succeeded, unblock the await below.
                    // If the process does exit later, TrySetResult is a no-op.
                    exitCompletion.TrySetCanceled(cancellationToken);
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
        catch (Win32Exception ex)
        {
            var message = $"Failed to start process '{command}': {ex.Message} (Win32 error {ex.NativeErrorCode})";
            _logger.LogWarning(ex, message);
            return new ProcessResult(-1, string.Empty, message);
        }
    }

    /// <summary>
    /// Runs a command that requires admin privileges. Attempts it directly first;
    /// if access is denied, elevates via a UAC prompt using Start-Process -Verb RunAs.
    /// </summary>
    public async Task<ProcessResult> RunElevatedAsync(
        string command,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(command, arguments, cancellationToken: cancellationToken);
        if (result.IsSuccess) return result;

        var output = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (!IsAccessDenied(output)) return result;

        _logger.LogInformation("Access denied — elevating via UAC: {Command} {Arguments}", command, arguments);

        // Escape single quotes in arguments for PowerShell
        var escapedCommand = command.Replace("'", "''");
        var escapedArguments = arguments.Replace("'", "''");
        var elevated = await RunAsync(
            "powershell",
            $"-NoProfile -Command \"Start-Process '{escapedCommand}' -ArgumentList '{escapedArguments}' -Verb RunAs -Wait -WindowStyle Hidden\"",
            cancellationToken: cancellationToken);

        return elevated;
    }

    /// <summary>
    /// Runs a PowerShell command that requires admin privileges. Attempts it directly first;
    /// if access is denied, elevates via a UAC prompt.
    /// </summary>
    public async Task<ProcessResult> RunPowerShellElevatedAsync(
        string psCommand,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            "powershell",
            $"-NoProfile -Command \"{psCommand}\"",
            cancellationToken: cancellationToken);

        if (result.IsSuccess) return result;

        var output = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (!IsAccessDenied(output)) return result;

        _logger.LogInformation("Access denied — elevating via UAC: {Command}", psCommand);

        var escapedCommand = psCommand.Replace("'", "''");
        return await RunAsync(
            "powershell",
            $"-NoProfile -Command \"Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile','-Command','{escapedCommand}'\"",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the current process PATH merged with user-level PATH entries from the registry.
    /// When running elevated, UAC only inherits the system PATH, so tools installed to user
    /// scope (winget packages, Scoop shims, etc.) are invisible to child processes without this.
    /// </summary>
    private static string GetAugmentedPath()
    {
        var processPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        try
        {
            if (!OperatingSystem.IsWindows())
                return processPath;

            var userPath = Registry.GetValue(@"HKEY_CURRENT_USER\Environment", "Path", null) as string;
            if (string.IsNullOrWhiteSpace(userPath))
                return processPath;

            // Merge, deduplicating entries while preserving order (process PATH wins for conflicts).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<string>();

            foreach (var entry in processPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(entry.Trim()))
                    merged.Add(entry.Trim());
            }

            foreach (var entry in userPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(entry.Trim()))
                    merged.Add(entry.Trim());
            }

            return string.Join(';', merged);
        }
        catch
        {
            // Registry read is best-effort — fall back to the unmodified process PATH.
            return processPath;
        }
    }

    private static bool IsAccessDenied(string output) =>
        (output.Contains("Access", StringComparison.OrdinalIgnoreCase) &&
         (output.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
          output.Contains("Cannot open", StringComparison.OrdinalIgnoreCase))) ||
        output.Contains("requires elevation", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Run as administrator", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("E_ACCESSDENIED", StringComparison.OrdinalIgnoreCase);
}
