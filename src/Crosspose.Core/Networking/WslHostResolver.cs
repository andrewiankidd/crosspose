using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Networking;

/// <summary>
/// Resolves the Windows host IP on the WSL-facing virtual Ethernet adapter.
/// Used for Linux→Windows container communication: Linux compose env vars reference
/// this IP via ${WSL_HOST_IP}, and port proxies on this interface forward to
/// Docker-mapped ports on localhost.
/// </summary>
public static class WslHostResolver
{
    /// <summary>
    /// Returns the IPv4 address of the vEthernet (WSL*) adapter.
    /// Falls back to querying WSL for its default gateway (which is the host IP).
    /// </summary>
    public static async Task<string?> ResolveAsync(ProcessRunner runner, CancellationToken cancellationToken)
    {
        // Try the .NET network interface approach first (no process spawn).
        var fromAdapter = GetWslAdapterAddress();
        if (!string.IsNullOrWhiteSpace(fromAdapter))
            return fromAdapter;

        // Fallback: ask WSL for its default gateway — that's the host IP.
        var result = await runner.RunAsync("wsl",
            $"-d {Configuration.CrossposeEnvironment.WslDistro} -- ip route show default",
            cancellationToken: cancellationToken);

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            // "default via 172.24.112.1 dev eth0"
            var parts = result.StandardOutput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var viaIdx = Array.IndexOf(parts, "via");
            if (viaIdx >= 0 && viaIdx + 1 < parts.Length && IPAddress.TryParse(parts[viaIdx + 1], out _))
                return parts[viaIdx + 1];
        }

        return null;
    }

    /// <summary>
    /// Resolves the WSL host IP synchronously from network adapters.
    /// </summary>
    public static string? GetWslAdapterAddress()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Match "vEthernet (WSL)" or "vEthernet (WSL (Hyper-V firewall))" etc.
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (!nic.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))
                    continue;

                var props = nic.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }
        }
        catch
        {
            // Best effort — fall back to WSL query.
        }

        return null;
    }
}
