using System.Text.Json;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Ensures the crosspose WSL distro is running Podman with cgroups v2 enabled.
/// </summary>
public sealed class PodmanCgroupCheck : ICheckFix
{
    public string Name => "podman-cgroups";
    public string Description => "Requires cgroups v2 for Podman inside the crosspose WSL distro.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var distro = CrossposeEnvironment.WslDistro;
        var result = await RunWslAsync(runner, cancellationToken, "-d", distro, "--", "podman", "info", "--format", "json");
        if (!result.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            error = string.IsNullOrWhiteSpace(error) ? "Unable to query podman info." : error.Trim();
            return CheckResult.Failure(error);
        }

        var json = ExtractJson(result.StandardOutput);
        if (json is null)
        {
            return CheckResult.Failure("Podman info output was empty or unparseable.");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("host", out var host) &&
                host.TryGetProperty("cgroupVersion", out var versionElement))
            {
                var value = versionElement.GetString()?.Trim();
                if (string.Equals(value, "v2", StringComparison.OrdinalIgnoreCase))
                {
                    return CheckResult.Success("Podman is using cgroups v2.");
                }

                value ??= "unknown";
                return CheckResult.Failure($"Podman is running with cgroups '{value}'. cgroups v2 is required.");
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse podman info output: {Output}", json);
            return CheckResult.Failure("Podman info JSON parsing failed.");
        }

        return CheckResult.Failure("Unable to determine podman cgroup version.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var (updated, error) = EnsureKernelCommandLine();
        if (!updated)
        {
            return FixResult.Failure(error);
        }

        var shutdown = await runner.RunAsync("wsl", "--shutdown", cancellationToken: cancellationToken);
        if (!shutdown.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(shutdown.StandardError)
                ? "Failed to restart WSL after updating configuration."
                : shutdown.StandardError.Trim();
            return FixResult.Failure(message);
        }

        return FixResult.Success("Enabled cgroups v2 via .wslconfig. Reopen Crosspose to restart the crosspose-data distro.");
    }

    private static (bool Updated, string Message) EnsureKernelCommandLine()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            return (false, "Unable to determine user profile path for .wslconfig.");
        }

        var path = Path.Combine(profile, ".wslconfig");
        var directive = "kernelCommandLine=cgroup_no_v1=all";
        var lines = File.Exists(path)
            ? new List<string>(File.ReadAllLines(path))
            : new List<string>();

        var output = new List<string>();
        var inWsl2 = false;
        var directiveWritten = false;
        var sectionFound = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var isSection = trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal);
            if (isSection)
            {
                if (inWsl2 && !directiveWritten)
                {
                    output.Add(directive);
                    directiveWritten = true;
                }
                inWsl2 = trimmed.Equals("[wsl2]", StringComparison.OrdinalIgnoreCase);
                if (inWsl2) sectionFound = true;
                output.Add(line);
                continue;
            }

            if (inWsl2 && trimmed.StartsWith("kernelCommandLine", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(directive);
                directiveWritten = true;
            }
            else
            {
                output.Add(line);
            }
        }

        if (!sectionFound)
        {
            if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(string.Empty);
            }
            output.Add("[wsl2]");
            output.Add(directive);
            directiveWritten = true;
        }
        else if (inWsl2 && !directiveWritten)
        {
            output.Add(directive);
            directiveWritten = true;
        }

        File.WriteAllLines(path, output);
        return (true, string.Empty);
    }

    private static string? ExtractJson(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var start = output.IndexOf('{');
        if (start < 0) return null;
        var json = output[start..].Trim();
        var end = json.LastIndexOf('}');
        if (end < 0) return null;
        return json[..(end + 1)];
    }

    private static Task<ProcessResult> RunWslAsync(ProcessRunner runner, CancellationToken cancellationToken, params string[] args)
    {
        var wsl = new WslRunner(runner);
        return wsl.ExecAsync(args, cancellationToken: cancellationToken);
    }
}
