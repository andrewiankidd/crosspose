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

    /// <summary>
    /// Workload name prefixes that should always be treated as Windows, regardless of what
    /// nodeSelector the Helm chart emits. Use this when the chart doesn't set
    /// kubernetes.io/os: windows but the workload is Windows-based.
    /// </summary>
    [YamlMember(Alias = "windows-workloads")]
    public List<string> WindowsWorkloads { get; set; } = new();

    /// <summary>
    /// Glob patterns matching service hostnames that should be rewritten when --remap-ports
    /// is active. Supports a single <c>*</c> wildcard in the subdomain portion.
    /// Example: <c>local-*.example.com</c> matches <c>local-dev-svc-core.example.com</c>.
    /// Services present in the rendered chart are remapped to <c>http://localhost:&lt;port&gt;</c>;
    /// services absent (disabled in values) are blanked.
    /// </summary>
    [YamlMember(Alias = "local-service-match")]
    public List<string> LocalServiceMatch { get; set; } = new();

    /// <summary>
    /// Explicit URL replacements applied after automatic port remapping.
    /// Use this for external upstream URLs (services not deployed by this chart) that should
    /// be rewritten to a specific local address or cleared.
    /// </summary>
    [YamlMember(Alias = "url-overrides")]
    public List<DekomposeUrlOverride> UrlOverrides { get; set; } = new();
}

public sealed class DekomposeUrlOverride
{
    /// <summary>The URL fragment to find (exact substring match, case-insensitive).</summary>
    [YamlMember(Alias = "from")]
    public string From { get; set; } = string.Empty;

    /// <summary>The replacement value. Use an empty string to blank the URL.</summary>
    [YamlMember(Alias = "to")]
    public string To { get; set; } = string.Empty;
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
