using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Networking;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public sealed class PortProxyCheck : ICheckFix
{
    private readonly int _listenPort;
    private readonly int _connectPort;
    private readonly string _additionalKey;
    private readonly string? _network;

    public PortProxyCheck(int listenPort, int connectPort, string? network = null)
    {
        if (listenPort <= 0) throw new ArgumentOutOfRangeException(nameof(listenPort));
        if (connectPort <= 0) throw new ArgumentOutOfRangeException(nameof(connectPort));
        _listenPort = listenPort;
        _connectPort = connectPort;
        _network = string.IsNullOrWhiteSpace(network) ? null : network.Trim();
        _additionalKey = PortProxyKey.Format(listenPort, connectPort, _network);
    }

    public string Name => $"port-proxy-{_listenPort}";
    public string Description => _connectPort != _listenPort
        ? $"Ensures Windows port proxy forwards {_listenPort} on the NAT gateway to localhost:{_connectPort} for Docker↔WSL2 container communication."
        : $"Ensures Windows port proxy exposes {_listenPort} to Docker Windows containers.";
    public bool IsAdditional => true;
    public string AdditionalKey => _additionalKey;
    public bool CanFix => true;
    public bool AutoFix => true;
    public int CheckIntervalSeconds => 120;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var natAddresses = await ResolveNatGatewayAddressesAsync(runner, cancellationToken);
        if (!natAddresses.Any())
        {
            return CheckResult.Failure("Unable to determine the Windows NAT gateway address.");
        }

        var proxyResult = await runner.RunAsync("netsh", "interface portproxy show v4tov4", cancellationToken: cancellationToken);
        if (!proxyResult.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(proxyResult.StandardError) ? proxyResult.StandardOutput : proxyResult.StandardError;
            message = string.IsNullOrWhiteSpace(message) ? "Failed to query port proxy configuration." : message.Trim();
            return CheckResult.Failure(message);
        }

        var missingProxy = natAddresses
            .Where(address => !PortProxyExists(proxyResult.StandardOutput, address, _listenPort, _connectPort))
            .ToList();

        if (missingProxy.Any())
        {
            return CheckResult.Failure($"Port proxy {_listenPort}→{_connectPort} is missing for NAT address(es): {string.Join(", ", missingProxy)}.");
        }

        // Also verify the Windows Firewall inbound rule exists — without it, Windows containers
        // cannot reach the portproxy listener even though the rule is configured in netsh.
        var fwResult = await runner.RunAsync("netsh", "advfirewall firewall show rule name=all dir=in", cancellationToken: cancellationToken);
        var missingFirewall = natAddresses
            .Where(address => !FirewallRuleExists(fwResult.StandardOutput, address, _listenPort))
            .ToList();

        if (missingFirewall.Any())
        {
            return CheckResult.Failure(
                $"Port proxy {_listenPort} is configured but missing Windows Firewall inbound rule for {string.Join(", ", missingFirewall)}. " +
                "Windows containers cannot reach the portproxy listener.");
        }

        return CheckResult.Success($"Port proxy {_listenPort}→{_connectPort} is configured for NAT address(es): {string.Join(", ", natAddresses)}.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var natAddresses = await ResolveNatGatewayAddressesAsync(runner, cancellationToken);
        if (!natAddresses.Any())
        {
            return FixResult.Failure("Unable to determine the Windows NAT gateway address.");
        }

        var existingConfig = await runner.RunAsync("netsh", "interface portproxy show v4tov4", cancellationToken: cancellationToken);
        if (!existingConfig.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(existingConfig.StandardError)
                ? existingConfig.StandardOutput
                : existingConfig.StandardError;
            message = string.IsNullOrWhiteSpace(message) ? "Failed to query port proxy configuration." : message.Trim();
            return FixResult.Failure(message);
        }

        var missingAddresses = natAddresses
            .Where(address => !PortProxyExists(existingConfig.StandardOutput, address, _listenPort, _connectPort))
            .ToList();

        foreach (var address in missingAddresses)
        {
            // Delete any existing rule for this listen address:port first — netsh add
            // silently fails if a rule with the same listen address:port already exists
            // with a different connect port.
            await runner.RunElevatedAsync("netsh",
                $"interface portproxy delete v4tov4 listenaddress={address} listenport={_listenPort}",
                cancellationToken);

            var addProxyArgs =
                $"interface portproxy add v4tov4 listenaddress={address} listenport={_listenPort} connectaddress=127.0.0.1 connectport={_connectPort}";
            var proxyResult = await runner.RunElevatedAsync("netsh", addProxyArgs, cancellationToken);
            if (!proxyResult.IsSuccess && !ContainsAlreadyExists(proxyResult))
            {
                var error = string.IsNullOrWhiteSpace(proxyResult.StandardError)
                    ? proxyResult.StandardOutput
                    : proxyResult.StandardError;
                error = string.IsNullOrWhiteSpace(error) ? "Failed to configure port proxy." : error.Trim();
                return FixResult.Failure(error);
            }
        }

        foreach (var address in natAddresses)
        {
            var sanitizedAddress = address.Replace('.', '-');
            var ruleName = $"port-proxy-{_listenPort}-{sanitizedAddress}";
            var addRuleArgs =
                $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localip={address} localport={_listenPort}";
            var ruleResult = await runner.RunElevatedAsync("netsh", addRuleArgs, cancellationToken);
            if (!ruleResult.IsSuccess && !ContainsAlreadyExists(ruleResult))
            {
                var error = string.IsNullOrWhiteSpace(ruleResult.StandardError)
                    ? ruleResult.StandardOutput
                    : ruleResult.StandardError;
                error = string.IsNullOrWhiteSpace(error) ? "Failed to add firewall rule." : error.Trim();
                return FixResult.Failure(error);
            }
        }

        return FixResult.Success($"Configured port proxy {_listenPort}→{_connectPort} on NAT address(es): {string.Join(", ", natAddresses)}.");
    }

    private static bool ContainsAlreadyExists(ProcessResult result)
    {
        var combined = new StringBuilder(result.StandardError)
            .AppendLine(result.StandardOutput)
            .ToString();
        return combined.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool FirewallRuleExists(string output, string listenAddress, int listenPort)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;

        // netsh advfirewall show rule output groups properties per rule separated by blank lines.
        // Look for a rule block containing both our LocalIP and LocalPort.
        var ruleName = $"port-proxy-{listenPort}-{listenAddress.Replace('.', '-')}";
        return output.IndexOf(ruleName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool PortProxyExists(string output, string listenAddress, int listenPort, int connectPort)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;

        var rows = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

        foreach (var row in rows)
        {
            if (row.Contains("Address", StringComparison.OrdinalIgnoreCase) &&
                row.Contains("Port", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // netsh output columns: listenAddress listenPort connectAddress connectPort
            var tokens = row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 4) continue;

            var rowListenAddr = tokens[0];
            var rowListenPort = tokens[1];
            var rowConnectPort = tokens[3];

            if (rowListenAddr.Equals(listenAddress, StringComparison.OrdinalIgnoreCase) &&
                rowListenPort.Equals(listenPort.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase) &&
                rowConnectPort.Equals(connectPort.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<string>> ResolveNatGatewayAddressesAsync(ProcessRunner runner, CancellationToken cancellationToken)
    {
        return await NatGatewayResolver.ResolveAsync(runner, cancellationToken, GetConfiguredNetworkName());
    }

    private string? GetConfiguredNetworkName()
    {
        if (!string.IsNullOrWhiteSpace(_network))
        {
            return _network;
        }
        return null;
    }
}
