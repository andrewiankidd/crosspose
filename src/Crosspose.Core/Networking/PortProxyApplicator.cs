using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Core.Networking;

/// <summary>
/// Applies portproxy and firewall rules needed for Docker↔WSL2 container communication.
/// Called automatically by ComposeOrchestrator after a successful 'up' — if the process is
/// elevated it acts immediately; otherwise it signals the caller to re-run with admin rights.
/// </summary>
public static class PortProxyApplicator
{
    private const int InfraPortMin = 40000;
    private const int InfraPortMax = 49999;

    [SupportedOSPlatform("windows")]
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Applies all pending portproxy and firewall rules from crosspose.yml.
    /// Returns <see cref="PortProxyApplyResult.ElevationRequired"/> when the process is not elevated.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static async Task<PortProxyApplyResult> TryApplyAsync(
        ProcessRunner runner,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var config = CrossposeConfigurationStore.Load();
        var checks = config.Doctor?.AdditionalChecks ?? new List<string>();

        var requirements = checks
            .Select(k =>
            {
                if (!PortProxyKey.TryParse(k, out var lp, out var cp, out var net)) return null;
                if (lp <= 0 || cp <= 0) return null;
                return new PortProxyRequirement(lp, cp, net);
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        if (requirements.Count == 0)
        {
            return PortProxyApplyResult.NothingToDo();
        }

        var errors = new List<string>();

        // Step 1: remove stale high-port rules that have no WSL listener
        await RemoveStaleRulesAsync(runner, logger, cancellationToken);

        // Step 2: resolve NAT gateway addresses once (shared across all requirements)
        var natAddresses = await NatGatewayResolver.ResolveAsync(runner, cancellationToken);
        if (natAddresses.Count == 0)
        {
            return PortProxyApplyResult.Failure("Unable to determine the Windows NAT gateway address.");
        }

        // Step 3: query existing rules once
        var proxyOutput = await GetPortProxyOutputAsync(runner, cancellationToken);

        // Step 4: apply each requirement
        foreach (var req in requirements)
        {
            var missingProxy = natAddresses
                .Where(addr => !PortProxyRuleExists(proxyOutput, addr, req.ListenPort, req.ConnectPort))
                .ToList();

            foreach (var address in missingProxy)
            {
                var args = $"interface portproxy add v4tov4 listenaddress={address} listenport={req.ListenPort} connectaddress=127.0.0.1 connectport={req.ConnectPort}";
                var result = await runner.RunElevatedAsync("netsh", args, cancellationToken);
                if (!result.IsSuccess && !ContainsAlreadyExists(result))
                {
                    var err = (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim();
                    errors.Add($"portproxy {address}:{req.ListenPort}: {err}");
                    logger.LogWarning("Failed to add portproxy rule {Address}:{Port}: {Error}", address, req.ListenPort, err);
                }
                else
                {
                    logger.LogInformation("Added portproxy rule {Address}:{ListenPort}→{ConnectPort}", address, req.ListenPort, req.ConnectPort);
                }
            }

            // Firewall rules
            foreach (var address in natAddresses)
            {
                var ruleName = $"port-proxy-{req.ListenPort}-{address.Replace('.', '-')}";
                var fwArgs = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localip={address} localport={req.ListenPort}";
                var fwResult = await runner.RunElevatedAsync("netsh", fwArgs, cancellationToken);
                if (!fwResult.IsSuccess && !ContainsAlreadyExists(fwResult))
                {
                    var err = (string.IsNullOrWhiteSpace(fwResult.StandardError) ? fwResult.StandardOutput : fwResult.StandardError).Trim();
                    errors.Add($"firewall {address}:{req.ListenPort}: {err}");
                    logger.LogWarning("Failed to add firewall rule for {Address}:{Port}: {Error}", address, req.ListenPort, err);
                }
                else
                {
                    logger.LogInformation("Ensured firewall rule {RuleName}", ruleName);
                }
            }
        }

        // Step 5: apply reverse port proxy rules (Linux → Windows) on the WSL-facing interface
        var reverseRequirements = checks
            .Select(k =>
            {
                if (!Configuration.PortProxyKey.TryParse(k, out _, out _, out _)) return null;
                return k;
            })
            .Where(k => k is not null)
            .ToList();

        // Load reverse requirements from all deployment directories
        var deploymentRoot = Configuration.CrossposeEnvironment.DeploymentDirectory;
        if (Directory.Exists(deploymentRoot))
        {
            var reverseReqs = new List<Deployment.ReversePortProxyRequirement>();
            foreach (var projectDir in Directory.GetDirectories(deploymentRoot))
            {
                foreach (var versionDir in Directory.GetDirectories(projectDir))
                {
                    reverseReqs.AddRange(Deployment.PortProxyRequirementLoader.LoadReverse(versionDir));
                }
                // Also check the project dir itself (flat structure)
                reverseReqs.AddRange(Deployment.PortProxyRequirementLoader.LoadReverse(projectDir));
            }

            if (reverseReqs.Count > 0)
            {
                var wslHostIp = await WslHostResolver.ResolveAsync(runner, cancellationToken);
                if (!string.IsNullOrWhiteSpace(wslHostIp))
                {
                    proxyOutput = await GetPortProxyOutputAsync(runner, cancellationToken);
                    foreach (var rev in reverseReqs)
                    {
                        if (PortProxyRuleExists(proxyOutput, wslHostIp!, rev.Port, rev.ConnectPort))
                            continue;

                        // Delete any existing rule for this listen address:port first
                        await runner.RunElevatedAsync("netsh",
                            $"interface portproxy delete v4tov4 listenaddress={wslHostIp} listenport={rev.Port}",
                            cancellationToken);

                        var args = $"interface portproxy add v4tov4 listenaddress={wslHostIp} listenport={rev.Port} connectaddress=127.0.0.1 connectport={rev.ConnectPort}";
                        var result = await runner.RunElevatedAsync("netsh", args, cancellationToken);
                        if (!result.IsSuccess && !ContainsAlreadyExists(result))
                        {
                            var err = (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim();
                            errors.Add($"reverse portproxy {wslHostIp}:{rev.Port}: {err}");
                        }
                        else
                        {
                            logger.LogInformation("Added reverse portproxy rule {Address}:{ListenPort}→{ConnectPort}", wslHostIp, rev.Port, rev.ConnectPort);
                        }

                        // Firewall rule on WSL interface
                        var fwName = $"port-proxy-{rev.Port}-{wslHostIp!.Replace('.', '-')}";
                        var fwArgs = $"advfirewall firewall add rule name=\"{fwName}\" dir=in action=allow protocol=TCP localip={wslHostIp} localport={rev.Port}";
                        await runner.RunElevatedAsync("netsh", fwArgs, cancellationToken);
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            return PortProxyApplyResult.Failure(string.Join("; ", errors));
        }

        return PortProxyApplyResult.Applied(requirements.Count);
    }

    private static async Task RemoveStaleRulesAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var proxyOutput = await GetPortProxyOutputAsync(runner, cancellationToken);
        if (string.IsNullOrWhiteSpace(proxyOutput)) return;

        var candidates = ParsePortProxyRules(proxyOutput)
            .Where(r => r.ConnectAddress.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                        && r.ConnectPort >= InfraPortMin
                        && r.ConnectPort <= InfraPortMax)
            .ToList();

        if (candidates.Count == 0) return;

        var wslListeners = await GetWslListeningPortsAsync(runner, cancellationToken);
        if (wslListeners is null) return; // WSL not running — skip stale cleanup

        foreach (var rule in candidates.Where(r => !wslListeners.Contains(r.ConnectPort)))
        {
            var delArgs = $"interface portproxy delete v4tov4 listenaddress={rule.ListenAddress} listenport={rule.ListenPort}";
            var result = await runner.RunElevatedAsync("netsh", delArgs, cancellationToken);
            if (result.IsSuccess)
            {
                logger.LogInformation("Removed stale portproxy rule {Address}:{Port}→{ConnectPort}", rule.ListenAddress, rule.ListenPort, rule.ConnectPort);
            }
        }
    }

    private static async Task<string?> GetPortProxyOutputAsync(ProcessRunner runner, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("netsh", "interface portproxy show v4tov4", cancellationToken: cancellationToken);
        return result.IsSuccess ? result.StandardOutput : null;
    }

    private static async Task<HashSet<int>?> GetWslListeningPortsAsync(ProcessRunner runner, CancellationToken cancellationToken)
    {
        var distro = Configuration.CrossposeEnvironment.WslDistro;
        var result = await runner.RunAsync("wsl", $"-d {distro} -- sh -c \"ss -tlnp 2>/dev/null || netstat -tlnp 2>/dev/null\"", cancellationToken: cancellationToken);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput)) return null;

        var ports = new HashSet<int>();
        foreach (var line in result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // ss -tlnp columns:     Netid State Recv-Q Send-Q Local-Address:Port Peer-Address:Port Process
            // netstat -tlnp columns: Proto Recv-Q Send-Q Local-Address:Port Foreign-Address State PID/Program
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
                    break;
                }
            }
        }
        return ports;
    }

    private static bool PortProxyRuleExists(string? output, string listenAddress, int listenPort, int connectPort)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        foreach (var rule in ParsePortProxyRules(output))
        {
            if (rule.ListenAddress.Equals(listenAddress, StringComparison.OrdinalIgnoreCase)
                && rule.ListenPort == listenPort
                && rule.ConnectPort == connectPort)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<PortProxyEntry> ParsePortProxyRules(string output)
    {
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("Address", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains("Port", StringComparison.OrdinalIgnoreCase)) continue;

            var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 4) continue;
            if (!int.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var lp)) continue;
            if (!int.TryParse(tokens[3], NumberStyles.None, CultureInfo.InvariantCulture, out var cp)) continue;
            yield return new PortProxyEntry(tokens[0], lp, tokens[2], cp);
        }
    }

    private static bool ContainsAlreadyExists(ProcessResult result)
    {
        var combined = (result.StandardError ?? "") + (result.StandardOutput ?? "");
        return combined.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed record PortProxyEntry(string ListenAddress, int ListenPort, string ConnectAddress, int ConnectPort);
    private sealed record PortProxyRequirement(int ListenPort, int ConnectPort, string? Network);
}

public sealed class PortProxyApplyResult
{
    public enum ResultKind { Applied, NothingToDo, ElevationRequired, Failure }

    public ResultKind Kind { get; }
    public string? Message { get; }
    public int RulesApplied { get; }

    private PortProxyApplyResult(ResultKind kind, string? message = null, int rulesApplied = 0)
    {
        Kind = kind;
        Message = message;
        RulesApplied = rulesApplied;
    }

    public static PortProxyApplyResult Applied(int count) =>
        new(ResultKind.Applied, $"Applied portproxy rules for {count} service(s).", count);

    public static PortProxyApplyResult NothingToDo() =>
        new(ResultKind.NothingToDo);

    public static PortProxyApplyResult ElevationRequired() =>
        new(ResultKind.ElevationRequired, "Administrator rights are required to configure portproxy rules.");

    public static PortProxyApplyResult Failure(string message) =>
        new(ResultKind.Failure, message);
}
