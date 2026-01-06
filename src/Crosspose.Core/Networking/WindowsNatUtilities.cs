using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Crosspose.Core.Networking;

public static class WindowsNatUtilities
{
    public static string? GetNatGatewayAddress()
    {
        var addresses = GetNatGatewayAddresses();
        return ChoosePreferredAddress(addresses);
    }

    public static IReadOnlyList<string> GetNatGatewayAddresses()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Array.Empty<string>();
        }

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (!IsPotentialNatInterface(nic)) continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;
                    addresses.Add(unicast.Address.ToString());
                }
            }
        }
        catch
        {
            // ignored - caller will handle empty list.
        }

        return addresses.ToList();
    }

    private static string? ChoosePreferredAddress(IReadOnlyList<string> addresses)
    {
        if (addresses.Count == 0)
        {
            return null;
        }

        var tryOrder = new[] { "172.", "10.", "192.168." };
        foreach (var prefix in tryOrder)
        {
            var candidate = addresses.FirstOrDefault(addr => addr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return addresses.FirstOrDefault();
    }

    private static bool IsPotentialNatInterface(NetworkInterface nic)
    {
        var name = nic.Name ?? string.Empty;
        var description = nic.Description ?? string.Empty;

        if (name.Contains("(nat)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) &&
            description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
