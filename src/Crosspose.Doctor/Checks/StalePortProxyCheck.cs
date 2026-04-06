using System.Globalization;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Detects portproxy rules pointing to high ports (40000–49999) in WSL2 that
/// no longer have an active listener. Crosspose assigns infra containers to these
/// high ports to avoid WSL2 port reservation conflicts; rules left behind from
/// previous deployments waste kernel resources and can cause confusion.
/// </summary>
public sealed class StalePortProxyCheck : ICheckFix
{
    // Crosspose allocates infra host ports from this range.
    private const int InfraPortMin = 40000;
    private const int InfraPortMax = 49999;

    public bool AutoFix => true;
    public int CheckIntervalSeconds => 120;

    public string Name => "stale-port-proxies";
    public string Description => "Detects netsh portproxy rules targeting high ports (40000–49999) in WSL2 that no longer have an active listener. These rules are left behind by previous Dekompose deployments.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var stale = await FindStaleRulesAsync(runner, logger, cancellationToken);
        if (stale is null)
        {
            return CheckResult.Success("WSL2 is not running — cannot verify portproxy targets.");
        }

        if (stale.Count == 0)
        {
            return CheckResult.Success("No stale Crosspose portproxy rules found.");
        }

        var descriptions = stale.Select(r => $"{r.ListenAddress}:{r.ListenPort}→{r.ConnectPort}");
        return CheckResult.Failure(
            $"Found {stale.Count} stale portproxy rule(s) with no WSL2 listener: {string.Join(", ", descriptions)}.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var stale = await FindStaleRulesAsync(runner, logger, cancellationToken);
        if (stale is null)
        {
            return FixResult.Failure("WSL2 is not running — cannot verify portproxy targets before removing rules.");
        }

        if (stale.Count == 0)
        {
            return FixResult.Success("No stale portproxy rules to remove.");
        }

        var failures = new List<string>();
        foreach (var rule in stale)
        {
            var args = $"interface portproxy delete v4tov4 listenaddress={rule.ListenAddress} listenport={rule.ListenPort}";
            var result = await runner.RunElevatedAsync("netsh", args, cancellationToken);
            if (result.IsSuccess)
            {
                logger.LogInformation("Removed stale portproxy rule: {Address}:{Port}", rule.ListenAddress, rule.ListenPort);
            }
            else
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                failures.Add($"{rule.ListenAddress}:{rule.ListenPort}: {error.Trim()}");
            }
        }

        if (failures.Count > 0)
        {
            return FixResult.Failure($"Failed to remove {failures.Count} rule(s): {string.Join("; ", failures)}");
        }

        return FixResult.Success($"Removed {stale.Count} stale portproxy rule(s).");
    }

    /// <summary>
    /// Returns null if WSL2 is not running (cannot determine stale vs active).
    /// Returns an empty list if WSL2 is running and no stale rules exist.
    /// </summary>
    private static async Task<IReadOnlyList<PortProxyRule>?> FindStaleRulesAsync(
        ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var proxyResult = await runner.RunAsync(
            "netsh", "interface portproxy show v4tov4",
            cancellationToken: cancellationToken);

        if (!proxyResult.IsSuccess || string.IsNullOrWhiteSpace(proxyResult.StandardOutput))
        {
            return Array.Empty<PortProxyRule>();
        }

        var candidates = ParsePortProxyRules(proxyResult.StandardOutput)
            .Where(r => r.ConnectAddress.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                        && r.ConnectPort >= InfraPortMin
                        && r.ConnectPort <= InfraPortMax)
            .ToList();

        if (candidates.Count == 0)
        {
            return Array.Empty<PortProxyRule>();
        }

        var wslListeners = await GetWslListeningPortsAsync(runner, cancellationToken);
        if (wslListeners is null)
        {
            return null;
        }

        return candidates
            .Where(r => !wslListeners.Contains(r.ConnectPort))
            .ToList();
    }

    /// <summary>
    /// Returns null if WSL is not running or the command fails.
    /// Otherwise returns the set of TCP ports that have a listener in WSL2.
    /// </summary>
    private static async Task<HashSet<int>?> GetWslListeningPortsAsync(
        ProcessRunner runner, CancellationToken cancellationToken)
    {
        var distro = Crosspose.Core.Configuration.CrossposeEnvironment.WslDistro;
        var result = await runner.RunAsync("wsl", $"-d {distro} -- sh -c \"ss -tlnp 2>/dev/null || netstat -tlnp 2>/dev/null\"", cancellationToken: cancellationToken);

        // Exit code non-zero or output referencing "not running" indicates WSL is unavailable.
        if (!result.IsSuccess)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return new HashSet<int>();
        }

        var ports = new HashSet<int>();
        foreach (var line in result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // ss -tlnp columns:     Netid State Recv-Q Send-Q Local-Address:Port Peer-Address:Port Process
            // netstat -tlnp columns: Proto Recv-Q Send-Q Local-Address:Port Foreign-Address State PID/Program
            // Try to extract a port from any column that looks like address:port.
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var colonIdx = token.LastIndexOf(':');
                if (colonIdx < 0) continue;
                var portStr = token[(colonIdx + 1)..];
                if (portStr == "*") continue;
                if (int.TryParse(portStr, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port >= 1024)
                {
                    ports.Add(port);
                    break; // take the first (local) address:port match per line
                }
            }
        }

        return ports;
    }

    private static IEnumerable<PortProxyRule> ParsePortProxyRules(string output)
    {
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Skip header lines containing "Address" and "Port"
            if (trimmed.Contains("Address", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains("Port", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // netsh portproxy columns: listenAddress listenPort connectAddress connectPort
            var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 4) continue;

            if (!int.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var listenPort)) continue;
            if (!int.TryParse(tokens[3], NumberStyles.None, CultureInfo.InvariantCulture, out var connectPort)) continue;

            yield return new PortProxyRule(tokens[0], listenPort, tokens[2], connectPort);
        }
    }

    private sealed record PortProxyRule(string ListenAddress, int ListenPort, string ConnectAddress, int ConnectPort);
}
