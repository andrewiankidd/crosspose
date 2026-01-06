using System;
using System.Globalization;

namespace Crosspose.Core.Configuration;

public static class PortProxyKey
{
    private const string Prefix = "port-proxy:";

    public static string Format(int port, string? network)
    {
        if (port <= 0) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(network))
        {
            return $"{Prefix}{port}";
        }

        return $"{Prefix}{port}@{network}";
    }

    public static bool TryParse(string value, out int port, out string? network)
    {
        port = 0;
        network = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var payload = value[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(payload)) return false;

        string? networkPart = null;
        var atIndex = payload.IndexOf('@');
        var portPart = payload;
        if (atIndex >= 0)
        {
            portPart = payload[..atIndex];
            networkPart = payload[(atIndex + 1)..];
        }

        if (!int.TryParse(portPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort) || parsedPort <= 0)
        {
            return false;
        }

        port = parsedPort;
        network = string.IsNullOrWhiteSpace(networkPart) ? null : networkPart.Trim();
        return true;
    }
}
