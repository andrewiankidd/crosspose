using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Networking;

public static class NatGatewayResolver
{
    public static async Task<IReadOnlyList<string>> ResolveAsync(
        ProcessRunner runner,
        CancellationToken cancellationToken,
        string? networkName = null)
    {
        if (!string.IsNullOrWhiteSpace(networkName))
        {
            var networkAddresses = await QueryDockerNetworkGatewayAddressesAsync(runner, cancellationToken, networkName);
            if (networkAddresses.Count > 0)
            {
                return networkAddresses;
            }
        }

        var composeAddresses = await QueryComposeNetworkGatewayAddressesAsync(runner, cancellationToken);
        if (composeAddresses.Count > 0)
        {
            return composeAddresses;
        }

        var dockerAddresses = await QueryDockerNatGatewayAddressesAsync(runner, cancellationToken);
        if (dockerAddresses.Count > 0)
        {
            return dockerAddresses;
        }

        return WindowsNatUtilities.GetNatGatewayAddresses();
    }

    public static async Task<string?> ResolvePreferredGatewayAddressAsync(
        ProcessRunner runner,
        CancellationToken cancellationToken,
        string? networkName = null)
    {
        return (await ResolveAsync(runner, cancellationToken, networkName)).FirstOrDefault();
    }

    private static async Task<IReadOnlyList<string>> QueryDockerNatGatewayAddressesAsync(
        ProcessRunner runner,
        CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            "docker",
            "network inspect nat --format \"{{range .IPAM.Config}}{{.Gateway}}{{end}}\"",
            cancellationToken: cancellationToken);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<string>();
        }

        return result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => IPAddress.TryParse(line, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> QueryDockerNetworkGatewayAddressesAsync(
        ProcessRunner runner,
        CancellationToken cancellationToken,
        string networkName)
    {
        var result = await runner.RunAsync(
            "docker",
            $"network inspect {networkName} --format \"{{{{range .IPAM.Config}}}}{{{{.Gateway}}}}{{{{end}}}}\"",
            cancellationToken: cancellationToken);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<string>();
        }

        return result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => IPAddress.TryParse(line, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> QueryComposeNetworkGatewayAddressesAsync(
        ProcessRunner runner,
        CancellationToken cancellationToken)
    {
        var listResult = await runner.RunAsync(
            "docker",
            "network ls --filter driver=nat --format \"{{.Name}}\"",
            cancellationToken: cancellationToken);

        if (!listResult.IsSuccess || string.IsNullOrWhiteSpace(listResult.StandardOutput))
        {
            return Array.Empty<string>();
        }

        var names = listResult.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line.Contains("dekompose-", StringComparison.OrdinalIgnoreCase))
            .Select(name => (Name: name, Priority: ExtractNetworkPriority(name)))
            .OrderByDescending(entry => entry.Priority)
            .Select(entry => entry.Name)
            .ToList();

        foreach (var name in names)
        {
            var addresses = await QueryDockerNetworkGatewayAddressesAsync(runner, cancellationToken, name);
            if (addresses.Count > 0)
            {
                return addresses;
            }
        }

        return Array.Empty<string>();
    }

    private static long ExtractNetworkPriority(string networkName)
    {
        var marker = "_dekompose-";
        var index = networkName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return 0;

        var digits = networkName[(index + marker.Length)..]
            .TakeWhile(char.IsDigit)
            .ToArray();
        if (digits.Length == 0) return 0;

        if (long.TryParse(new string(digits), out var parsed))
        {
            return parsed;
        }

        return 0;
    }
}
