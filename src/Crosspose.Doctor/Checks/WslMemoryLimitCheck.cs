using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public sealed class WslMemoryLimitCheck : ICheckFix
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".wslconfig");

    public string Name => "wsl-memory-limit";
    public string Description => "Verifies WSL is not capped with a custom memory limit.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var lines = await ReadConfigLinesAsync(cancellationToken);
        if (!ContainsMemoryLimit(lines, out var line))
        {
            return CheckResult.Success("WSL memory limit is not configured.");
        }

        var message = string.IsNullOrWhiteSpace(line)
            ? "WSL memory limit is configured."
            : $"WSL memory limit is configured ({line}).";
        return CheckResult.Failure(message);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var lines = await ReadConfigLinesAsync(cancellationToken);
        if (!ContainsMemoryLimit(lines, out _))
        {
            return FixResult.Success("WSL memory limit is not set.");
        }

        var updatedLines = RemoveMemoryLimitLines(lines);
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllLinesAsync(ConfigPath, updatedLines, cancellationToken);

        var shutdownResult = await runner.RunAsync("wsl", "--shutdown", cancellationToken: cancellationToken);
        if (!shutdownResult.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(shutdownResult.StandardError)
                ? shutdownResult.StandardOutput
                : shutdownResult.StandardError;
            message = string.IsNullOrWhiteSpace(message)
                ? "Failed to restart WSL after removing the memory limit."
                : message;
            return FixResult.Failure(message);
        }

        return FixResult.Success("Removed the memory limit from WSL configuration and restarted WSL.");
    }

    private static async Task<IReadOnlyList<string>> ReadConfigLinesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ConfigPath))
        {
            return Array.Empty<string>();
        }

        return await File.ReadAllLinesAsync(ConfigPath, cancellationToken);
    }

    private static bool ContainsMemoryLimit(IReadOnlyList<string> lines, out string? matchLine)
    {
        foreach (var line in lines)
        {
            if (IsMemoryLimitLine(line))
            {
                matchLine = line.Trim();
                return true;
            }
        }

        matchLine = null;
        return false;
    }

    private static IEnumerable<string> RemoveMemoryLimitLines(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (IsMemoryLimitLine(line))
            {
                continue;
            }

            yield return line;
        }
    }

    private static bool IsMemoryLimitLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("#") || trimmed.StartsWith(";"))
        {
            return false;
        }

        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return false;
        }

        var key = trimmed.Substring(0, equalsIndex).Trim();
        return string.Equals(key, "memory", StringComparison.OrdinalIgnoreCase);
    }
}
