using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Detects and removes networkingMode=mirrored from ~/.wslconfig.
/// Mirrored networking causes WSL2 to reserve well-known ports on the Windows host,
/// which prevents netsh portproxy from creating real TCP socket listeners for those ports.
/// This breaks Docker-to-WSL2 communication via portproxy.
/// </summary>
public sealed class WslNetworkingModeCheck : ICheckFix
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".wslconfig");

    public string Name => "wsl-networking-mode";
    public string Description => "Ensures WSL2 networking mode is compatible with Docker↔WSL2 port proxying (not mirrored).";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var lines = await ReadConfigLinesAsync(cancellationToken);
        if (!ContainsMirroredMode(lines, out var line))
        {
            return CheckResult.Success("WSL2 networking mode is compatible (not mirrored).");
        }

        return CheckResult.Failure($"WSL2 is configured with mirrored networking ({line}). This prevents portproxy from bridging Docker containers to WSL2 services.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var lines = await ReadConfigLinesAsync(cancellationToken);
        if (!ContainsMirroredMode(lines, out _))
        {
            return FixResult.Success("WSL2 networking mode is not set to mirrored.");
        }

        var updatedLines = RemoveMirroredModeLines(lines);
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
                ? "Failed to restart WSL after updating networking configuration."
                : message;
            return FixResult.Failure(message);
        }

        return FixResult.Success("Removed mirrored networking mode from WSL configuration and restarted WSL. Portproxy will now create real socket listeners.");
    }

    private static async Task<IReadOnlyList<string>> ReadConfigLinesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ConfigPath))
        {
            return Array.Empty<string>();
        }

        return await File.ReadAllLinesAsync(ConfigPath, cancellationToken);
    }

    private static bool ContainsMirroredMode(IReadOnlyList<string> lines, out string? matchLine)
    {
        foreach (var line in lines)
        {
            if (IsMirroredModeLine(line))
            {
                matchLine = line.Trim();
                return true;
            }
        }

        matchLine = null;
        return false;
    }

    private static IEnumerable<string> RemoveMirroredModeLines(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (!IsMirroredModeLine(line))
            {
                yield return line;
            }
        }
    }

    private static bool IsMirroredModeLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("#") || trimmed.StartsWith(";")) return false;

        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex <= 0) return false;

        var key = trimmed[..equalsIndex].Trim();
        var value = trimmed[(equalsIndex + 1)..].Trim();

        return string.Equals(key, "networkingMode", StringComparison.OrdinalIgnoreCase)
            && string.Equals(value, "mirrored", StringComparison.OrdinalIgnoreCase);
    }
}
