using System.Globalization;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

/// <summary>
/// Detects Windows Firewall rules created by Crosspose (named port-proxy-{port}-{address})
/// that no longer have a corresponding netsh portproxy entry. These accumulate across
/// deployments as Docker's NAT adapter is recreated with a new IP, leaving firewall
/// holes open for addresses that no longer route anywhere.
/// </summary>
public sealed class StaleFirewallRuleCheck : ICheckFix
{
    private const string RulePrefix = "port-proxy-";

    public string Name => "stale-firewall-rules";
    public string Description => "Detects Crosspose firewall rules (port-proxy-*) that have no corresponding netsh portproxy entry. These accumulate when Docker's NAT adapter changes IP between deployments.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 300;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var stale = await FindStaleRuleNamesAsync(runner, cancellationToken);
        if (stale.Count == 0)
            return CheckResult.Success("No stale Crosspose firewall rules found.");
        return CheckResult.Failure(
            $"Found {stale.Count} stale firewall rule(s) with no portproxy entry: {string.Join(", ", stale)}");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var stale = await FindStaleRuleNamesAsync(runner, cancellationToken);
        if (stale.Count == 0)
            return FixResult.Success("No stale firewall rules to remove.");

        var failures = new List<string>();
        foreach (var name in stale)
        {
            var result = await runner.RunElevatedAsync(
                "netsh", $"advfirewall firewall delete rule name=\"{name}\"", cancellationToken);
            if (result.IsSuccess)
                logger.LogInformation("Removed stale firewall rule: {Rule}", name);
            else
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput : result.StandardError;
                logger.LogWarning("Failed to remove firewall rule {Rule}: {Error}", name, error.Trim());
                failures.Add(name);
            }
        }

        return failures.Count > 0
            ? FixResult.Failure($"Failed to remove {failures.Count} rule(s): {string.Join(", ", failures)}")
            : FixResult.Success($"Removed {stale.Count} stale firewall rule(s).");
    }

    private static async Task<IReadOnlyList<string>> FindStaleRuleNamesAsync(
        ProcessRunner runner, CancellationToken cancellationToken)
    {
        // Get all port-proxy-* firewall rule names via PowerShell (targeted, avoids parsing name=all)
        var fwResult = await runner.RunAsync(
            "powershell",
            "-NoProfile -Command \"(Get-NetFirewallRule -Name 'port-proxy-*' -ErrorAction SilentlyContinue).Name | Sort-Object -Unique\"",
            cancellationToken: cancellationToken);

        if (!fwResult.IsSuccess || string.IsNullOrWhiteSpace(fwResult.StandardOutput))
            return Array.Empty<string>();

        var firewallRuleNames = SplitLines(fwResult.StandardOutput)
            .Where(n => n.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (firewallRuleNames.Count == 0)
            return Array.Empty<string>();

        // Build the set of expected rule names from active portproxy entries
        var proxyResult = await runner.RunAsync(
            "netsh", "interface portproxy show v4tov4", cancellationToken: cancellationToken);

        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (proxyResult.IsSuccess && !string.IsNullOrWhiteSpace(proxyResult.StandardOutput))
        {
            foreach (var (address, port) in ParsePortProxyListenEntries(proxyResult.StandardOutput))
                expectedNames.Add($"{RulePrefix}{port}-{address.Replace('.', '-')}");
        }

        return firewallRuleNames
            .Where(r => !expectedNames.Contains(r))
            .OrderBy(r => r)
            .ToList();
    }

    private static IEnumerable<(string Address, int Port)> ParsePortProxyListenEntries(string output)
    {
        foreach (var line in SplitLines(output))
        {
            // netsh portproxy columns: listenAddress listenPort connectAddress connectPort
            if (line.Contains("Address", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Port", StringComparison.OrdinalIgnoreCase))
                continue;

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;
            if (!int.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                continue;

            yield return (tokens[0], port);
        }
    }

    private static IEnumerable<string> SplitLines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
