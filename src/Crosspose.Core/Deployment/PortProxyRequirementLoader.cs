using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Crosspose.Core.Deployment;

/// <summary>
/// A port proxy requirement emitted by Dekompose into conversion-report.yaml.
/// <see cref="Port"/> is the listen port on the NAT gateway (standard well-known port).
/// <see cref="ConnectPort"/> is the high host port that Podman actually binds to inside WSL2.
/// </summary>
public sealed record PortProxyRequirement(int Port, int ConnectPort, string? Network);

internal sealed class PortProxyRequirementEntry
{
    [YamlMember(Alias = "port")]
    public int Port { get; init; }

    [YamlMember(Alias = "connectPort")]
    public int ConnectPort { get; init; }

    [YamlMember(Alias = "network")]
    public string? Network { get; init; }
}

internal sealed class PortProxyReport
{
    [YamlMember(Alias = "portProxyRequirements")]
    public List<PortProxyRequirementEntry>? PortProxyRequirements { get; init; }
}

public static class PortProxyRequirementLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static IReadOnlyCollection<PortProxyRequirement> Load(string directory)
    {
        var path = Path.Combine(directory, "conversion-report.yaml");
        if (!File.Exists(path))
        {
            return Array.Empty<PortProxyRequirement>();
        }

        try
        {
            var yaml = File.ReadAllText(path);
            var report = Deserializer.Deserialize<PortProxyReport>(yaml);
            if (report?.PortProxyRequirements is not { Count: > 0 } entries)
            {
                return Array.Empty<PortProxyRequirement>();
            }

            return entries
                .Where(entry => entry.Port > 0)
                .GroupBy(entry => entry.Port)
                .Select(group =>
                {
                    var first = group.First();
                    // If no connectPort was encoded (legacy report), default to same as listen port
                    var connectPort = first.ConnectPort > 0 ? first.ConnectPort : first.Port;
                    return new PortProxyRequirement(group.Key, connectPort, first.Network);
                })
                .ToList();
        }
        catch
        {
            return Array.Empty<PortProxyRequirement>();
        }
    }
}
