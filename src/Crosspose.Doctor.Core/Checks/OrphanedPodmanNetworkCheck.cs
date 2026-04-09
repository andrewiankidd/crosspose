using System.Text.Json;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

/// <summary>
/// Detects Podman networks left behind by previous Dekompose deployments inside WSL.
/// Networks matching the *_dekompose-* naming convention with no attached containers
/// are orphaned and leave stale iptables DNAT rules that conflict with new deployments
/// using the same host ports — causing "socket hang up" / "empty reply" on those ports.
/// </summary>
public sealed class OrphanedPodmanNetworkCheck : ICheckFix
{
    private const string NetworkPattern = "_dekompose-";

    public string Name => "orphaned-podman-networks";
    public string Description => "Detects Podman networks from previous Dekompose deployments that have no attached containers. Stale networks leave iptables DNAT rules that conflict with new deployments on the same ports.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 120;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var orphaned = await FindOrphanedNetworksAsync(runner, cancellationToken);
        if (orphaned.Count == 0)
            return CheckResult.Success("No orphaned Dekompose Podman networks found.");

        return CheckResult.Failure(
            $"Found {orphaned.Count} orphaned Podman Dekompose network(s): {string.Join(", ", orphaned)}. " +
            "Stale iptables DNAT rules from these networks conflict with active port mappings.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var orphaned = await FindOrphanedNetworksAsync(runner, cancellationToken);
        if (orphaned.Count == 0)
            return FixResult.Success("No orphaned Podman Dekompose networks to remove.");

        var wsl = new WslRunner(runner);
        var distro = CrossposeEnvironment.WslDistro;
        var failures = new List<string>();

        foreach (var name in orphaned)
        {
            var result = await wsl.ExecAsync(
                ["-d", distro, "--", "podman", "network", "rm", name],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
                logger.LogInformation("Removed orphaned Podman network: {Network}", name);
            else
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                failures.Add($"{name}: {error.Trim()}");
            }
        }

        if (failures.Count > 0)
            return FixResult.Failure($"Failed to remove {failures.Count} network(s): {string.Join("; ", failures)}");

        return FixResult.Success(
            $"Removed {orphaned.Count} orphaned Podman Dekompose network(s): {string.Join(", ", orphaned)}.");
    }

    private static async Task<IReadOnlyList<string>> FindOrphanedNetworksAsync(
        ProcessRunner runner, CancellationToken cancellationToken)
    {
        var wsl = new WslRunner(runner);
        var distro = CrossposeEnvironment.WslDistro;

        // Use --format json for ls to avoid Go template quoting issues through wsl arg chain
        var lsResult = await wsl.ExecAsync(
            ["-d", distro, "--", "podman", "network", "ls", "--format", "json"],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!lsResult.IsSuccess || string.IsNullOrWhiteSpace(lsResult.StandardOutput))
            return Array.Empty<string>();

        var candidates = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(lsResult.StandardOutput);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("Name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(name) &&
                            name.Contains(NetworkPattern, StringComparison.OrdinalIgnoreCase))
                            candidates.Add(name);
                    }
                }
            }
        }
        catch { return Array.Empty<string>(); /* best-effort — podman JSON output may be malformed */ }

        if (candidates.Count == 0)
            return Array.Empty<string>();

        var orphaned = new List<string>();
        foreach (var name in candidates)
        {
            // Use json format — Go templates with {{}} get split by wsl.exe arg parsing
            var inspectResult = await wsl.ExecAsync(
                ["-d", distro, "--", "podman", "network", "inspect", "--format", "json", name],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!inspectResult.IsSuccess) continue;

            try
            {
                using var doc = JsonDocument.Parse(inspectResult.StandardOutput);
                var root = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray().FirstOrDefault()
                    : doc.RootElement;

                // Network is orphaned if it has no containers attached
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("containers", out var containers) &&
                    containers.ValueKind == JsonValueKind.Object &&
                    containers.EnumerateObject().Any())
                    continue; // has containers — not orphaned

                orphaned.Add(name);
            }
            catch { /* best-effort — skip networks whose inspect output is unparseable */ }
        }

        return orphaned;
    }
}
