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
    private readonly int _port;
    private readonly string _additionalKey;
    private readonly string? _network;

    public PortProxyCheck(int port, string? network = null)
    {
        if (port <= 0) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
        _network = string.IsNullOrWhiteSpace(network) ? null : network.Trim();
        _additionalKey = PortProxyKey.Format(port, _network);
    }

    public string Name => $"port-proxy-{_port}";
    public string Description => $"Ensures Windows port proxy exposes {_port} to Docker Windows containers.";
    public bool IsAdditional => true;
    public string AdditionalKey => _additionalKey;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var natAddresses = await ResolveNatGatewayAddressesAsync(runner, cancellationToken);
        if (!natAddresses.Any())
        {
            return CheckResult.Failure("Unable to determine the Windows NAT gateway address.");
        }

        var result = await runner.RunAsync("netsh", "interface portproxy show v4tov4", cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            message = string.IsNullOrWhiteSpace(message) ? "Failed to query port proxy configuration." : message.Trim();
            return CheckResult.Failure(message);
        }

        var missing = natAddresses
            .Where(address => !PortProxyExists(result.StandardOutput, address, _port))
            .ToList();

        if (missing.Any())
        {
            return CheckResult.Failure($"Port proxy {_port} is missing for NAT address(es): {string.Join(", ", missing)}.");
        }

        return CheckResult.Success($"Port proxy {_port} is configured for NAT address(es): {string.Join(", ", natAddresses)}.");
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
            .Where(address => !PortProxyExists(existingConfig.StandardOutput, address, _port))
            .ToList();

        foreach (var address in missingAddresses)
        {
            var addProxyArgs =
                $"interface portproxy add v4tov4 listenaddress={address} listenport={_port} connectaddress=127.0.0.1 connectport={_port}";
            var proxyResult = await runner.RunAsync("netsh", addProxyArgs, cancellationToken: cancellationToken);
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
            var ruleName = $"port-proxy-{_port}-{sanitizedAddress}";
            var addRuleArgs =
                $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localip={address} localport={_port}";
            var ruleResult = await runner.RunAsync("netsh", addRuleArgs, cancellationToken: cancellationToken);
            if (!ruleResult.IsSuccess && !ContainsAlreadyExists(ruleResult))
            {
                var error = string.IsNullOrWhiteSpace(ruleResult.StandardError)
                    ? ruleResult.StandardOutput
                    : ruleResult.StandardError;
                error = string.IsNullOrWhiteSpace(error) ? "Failed to add firewall rule." : error.Trim();
                return FixResult.Failure(error);
            }
        }

        return FixResult.Success($"Configured port proxy {_port} on NAT address(es): {string.Join(", ", natAddresses)}.");
    }

    private static bool ContainsAlreadyExists(ProcessResult result)
    {
        var combined = new StringBuilder(result.StandardError)
            .AppendLine(result.StandardOutput)
            .ToString();
        return combined.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool PortProxyExists(string output, string address, int port)
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

            var tokens = row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;

            var listenAddress = tokens[0];
            var listenPort = tokens[1];

            if (listenAddress.Equals(address, StringComparison.OrdinalIgnoreCase) &&
                listenPort.Equals(port.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<string>> ResolveNatGatewayAddressesAsync(ProcessRunner runner, CancellationToken cancellationToken)
    {
        var natAddresses = await NatGatewayResolver.ResolveAsync(runner, cancellationToken, GetConfiguredNetworkName());
        return natAddresses;
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
