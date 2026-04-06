using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

/// <summary>
/// Detects Docker networks left behind by previous Dekompose deployments.
/// Networks matching the *_dekompose-* naming convention with no attached containers
/// are orphaned and can cause Windows HNS errors (0x32) when a new deployment tries
/// to create a network.
/// </summary>
public sealed class OrphanedDockerNetworkCheck : ICheckFix
{
    private const string NetworkPattern = "_dekompose-";

    public string Name => "orphaned-docker-networks";
    public string Description => "Detects Docker networks from previous Dekompose deployments that have no attached containers. Stale networks can cause HNS errors when creating new deployments.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 120;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var orphaned = await FindOrphanedNetworksAsync(runner, cancellationToken);
        if (orphaned.Count == 0)
        {
            return CheckResult.Success("No orphaned Dekompose Docker networks found.");
        }

        return CheckResult.Failure(
            $"Found {orphaned.Count} orphaned Dekompose network(s): {string.Join(", ", orphaned)}. " +
            "These can cause HNS errors on next deployment.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var orphaned = await FindOrphanedNetworksAsync(runner, cancellationToken);
        if (orphaned.Count == 0)
        {
            return FixResult.Success("No orphaned Dekompose networks to remove.");
        }

        var failures = new List<string>();
        foreach (var name in orphaned)
        {
            var result = await runner.RunAsync("docker", "network rm \"" + name + "\"", cancellationToken: cancellationToken);
            if (result.IsSuccess)
            {
                logger.LogInformation("Removed orphaned Docker network: {Network}", name);
            }
            else
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                failures.Add($"{name}: {error.Trim()}");
            }
        }

        if (failures.Count > 0)
        {
            return FixResult.Failure($"Failed to remove {failures.Count} network(s): {string.Join("; ", failures)}");
        }

        return FixResult.Success(
            $"Removed {orphaned.Count} orphaned Dekompose network(s): {string.Join(", ", orphaned)}.");
    }

    private static async Task<IReadOnlyList<string>> FindOrphanedNetworksAsync(
        ProcessRunner runner, CancellationToken cancellationToken)
    {
        var lsResult = await runner.RunAsync(
            "docker", "network ls --format \"{{.Name}}\"",
            cancellationToken: cancellationToken);

        if (!lsResult.IsSuccess || string.IsNullOrWhiteSpace(lsResult.StandardOutput))
        {
            return Array.Empty<string>();
        }

        var candidates = lsResult.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n.Contains(NetworkPattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        var orphaned = new List<string>();
        foreach (var name in candidates)
        {
            var inspectResult = await runner.RunAsync(
                "docker", "network inspect --format \"{{len .Containers}}\" \"" + name + "\"",
                cancellationToken: cancellationToken);

            if (!inspectResult.IsSuccess) continue;

            var countStr = inspectResult.StandardOutput.Trim();
            if (int.TryParse(countStr, out var count) && count == 0)
            {
                orphaned.Add(name);
            }
        }

        return orphaned;
    }
}
