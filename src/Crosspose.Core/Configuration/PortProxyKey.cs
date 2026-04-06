using System;
using System.Globalization;

namespace Crosspose.Core.Configuration;

public static class PortProxyKey
{
    private const string Prefix = "port-proxy:";

    /// <summary>
    /// Formats a key encoding both the listen port (what Docker containers connect to on the NAT
    /// gateway) and the connect port (the high host port Podman binds to inside WSL2).
    /// Format: "port-proxy:{listenPort}>{connectPort}@{network}" or "port-proxy:{port}" when ports match.
    /// </summary>
    public static string Format(int listenPort, int connectPort, string? network)
    {
        if (listenPort <= 0) throw new ArgumentOutOfRangeException(nameof(listenPort));
        if (connectPort <= 0) throw new ArgumentOutOfRangeException(nameof(connectPort));

        var portPart = connectPort != listenPort
            ? $"{listenPort}>{connectPort}"
            : $"{listenPort}";

        if (string.IsNullOrWhiteSpace(network))
        {
            return $"{Prefix}{portPart}";
        }

        return $"{Prefix}{portPart}@{network}";
    }

    /// <summary>Backward-compatible overload where listenPort == connectPort.</summary>
    public static string Format(int port, string? network) => Format(port, port, network);

    /// <summary>
    /// Parses a key produced by <see cref="Format(int,int,string?)"/>.
    /// If no connect port is encoded, <paramref name="connectPort"/> equals <paramref name="listenPort"/>.
    /// </summary>
    public static bool TryParse(string value, out int listenPort, out int connectPort, out string? network)
    {
        listenPort = 0;
        connectPort = 0;
        network = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var payload = value[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(payload)) return false;

        string? networkPart = null;
        var atIndex = payload.IndexOf('@');
        var portSection = payload;
        if (atIndex >= 0)
        {
            portSection = payload[..atIndex];
            networkPart = payload[(atIndex + 1)..];
        }

        var gtIndex = portSection.IndexOf('>');
        string portPart;
        string? connectPortPart;
        if (gtIndex >= 0)
        {
            portPart = portSection[..gtIndex];
            connectPortPart = portSection[(gtIndex + 1)..];
        }
        else
        {
            portPart = portSection;
            connectPortPart = null;
        }

        if (!int.TryParse(portPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedListen) || parsedListen <= 0)
            return false;

        listenPort = parsedListen;

        if (connectPortPart is not null)
        {
            if (!int.TryParse(connectPortPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedConnect) || parsedConnect <= 0)
                return false;
            connectPort = parsedConnect;
        }
        else
        {
            connectPort = parsedListen;
        }

        network = string.IsNullOrWhiteSpace(networkPart) ? null : networkPart.Trim();
        return true;
    }

    /// <summary>Backward-compatible overload that discards the connect port.</summary>
    public static bool TryParse(string value, out int port, out string? network)
    {
        var ok = TryParse(value, out port, out _, out network);
        return ok;
    }
}
