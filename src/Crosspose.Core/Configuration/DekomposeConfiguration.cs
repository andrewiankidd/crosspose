using System.Linq;
using YamlDotNet.Serialization;

namespace Crosspose.Core.Configuration;

public sealed class DekomposeConfiguration
{
    [YamlMember(Alias = "custom-rules")]
    public List<DekomposeRuleSet> CustomRules { get; set; } = new();
}

public sealed class DekomposeRuleSet
{
    [YamlMember(Alias = "match")]
    public string Match { get; set; } = "*";

    [YamlMember(Alias = "infra")]
    public List<DekomposeInfraDefinition> Infrastructure { get; set; } = new();

    [YamlMember(Alias = "secret-key-refs")]
    public Dictionary<string, List<DekomposeSecretDefinition>> SecretKeyRefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DekomposeInfraDefinition
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "image")]
    public string Image { get; set; } = string.Empty;

    [YamlMember(Alias = "command")]
    public string? Command { get; set; }

    [YamlMember(Alias = "environment")]
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [YamlMember(Alias = "ports")]
    public List<object> PortsRaw { get; set; } = new();

    [YamlMember(Alias = "healthcheck")]
    public Dictionary<string, object?>? Healthcheck { get; set; }

    [YamlMember(Alias = "volumes")]
    public List<string> Volumes { get; set; } = new();

    [YamlMember(Alias = "build")]
    public Dictionary<string, object?>? Build { get; set; }

    [YamlMember(Alias = "compose-file")]
    public string? ComposeFile { get; set; }

    [YamlMember(Alias = "os")]
    public string? Os { get; set; }

    [YamlIgnore]
    public List<string> Ports => PortsRaw
        .Select(p => p?.ToString() ?? string.Empty)
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .ToList();
}

public sealed class DekomposeSecretDefinition
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "literal";

    [YamlMember(Alias = "options")]
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
