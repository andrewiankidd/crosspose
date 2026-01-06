using System.Collections.Generic;
using Crosspose.Core.Orchestration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Crosspose.Core.Configuration;

public sealed class CrossposeConfiguration
{
    [YamlMember(Alias = "compose")]
    public ComposeConfiguration Compose { get; set; } = new();

    [YamlMember(Alias = "oci-registries")]
    public List<OciRegistryEntry> OciRegistries { get; set; } = new();

    [YamlMember(Alias = "helm-repo")]
    public List<HelmRepositoryEntry> HelmRepositories { get; set; } = new();

    [YamlMember(Alias = "doctor")]
    public DoctorConfiguration Doctor { get; set; } = new();

    [YamlMember(Alias = "dekompose")]
    public DekomposeConfiguration Dekompose { get; set; } = new();
}

public sealed class ComposeConfiguration
{
    [YamlMember(Alias = "output-directory")]
    public string? OutputDirectory { get; set; }

    [YamlMember(Alias = "deployment-directory")]
    public string? DeploymentDirectory { get; set; }

    [YamlMember(Alias = "log-file")]
    public string? LogFile { get; set; }

    [YamlMember(Alias = "gui")]
    public ComposeGuiConfiguration Gui { get; set; } = new();

    [YamlMember(Alias = "wsl")]
    public ComposeWslConfiguration Wsl { get; set; } = new();
}

public sealed class ComposeGuiConfiguration
{
    [YamlMember(Alias = "refresh-interval-seconds")]
    public int? RefreshIntervalSeconds { get; set; }
}

public sealed class ComposeWslConfiguration
{
    [YamlMember(Alias = "distro")]
    public string? Distro { get; set; }

    [YamlMember(Alias = "user")]
    public string? User { get; set; }

    [YamlMember(Alias = "password")]
    public string? Password { get; set; }
}

public sealed class DoctorConfiguration
{
    [YamlMember(Alias = "additionalChecks")]
    public List<string> AdditionalChecks { get; set; } = new();

    [YamlMember(Alias = "optionalChecks")]
    public List<string>? LegacyOptionalChecks
    {
        set
        {
            if (value is { Count: > 0 } && (AdditionalChecks is null || AdditionalChecks.Count == 0))
            {
                AdditionalChecks = value;
            }
        }
    }
}

public sealed class HelmRepositoryEntry
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "filter")]
    public string? Filter { get; set; }
}

public static class CrossposeConfigurationStore
{
    private static readonly object Sync = new();
    private static readonly string ConfigPathValue = LocateConfigPath();
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .Build();

    public static string ConfigPath => ConfigPathValue;

    public static CrossposeConfiguration Load()
    {
        lock (Sync)
        {
            if (File.Exists(ConfigPathValue))
            {
                try
                {
                    var yaml = File.ReadAllText(ConfigPathValue);
                    var cfg = Deserializer.Deserialize<CrossposeConfiguration>(yaml);
                    if (cfg is not null) return cfg;
                }
                catch (Exception ex)
                {
                    var message =
                        $"Failed to load Crosspose configuration at '{ConfigPathValue}'. " +
                        "Ensure the YAML is valid (wrap values that start with '{{' or contain other special characters in quotes).";
                    throw new InvalidOperationException(message, ex);
                }
            }

            var config = new CrossposeConfiguration();
            Save(config);
            return config;
        }
    }

    public static void Save(CrossposeConfiguration configuration)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPathValue)!);
            var yaml = Serializer.Serialize(configuration ?? new CrossposeConfiguration());
            File.WriteAllText(ConfigPathValue, yaml);
        }
    }

    public static void MergeDekomposeConfiguration(string dekomposeConfigPath)
    {
        if (string.IsNullOrWhiteSpace(dekomposeConfigPath))
        {
            throw new ArgumentException("Dekompose config path is required.", nameof(dekomposeConfigPath));
        }

        if (!File.Exists(dekomposeConfigPath))
        {
            throw new FileNotFoundException("Dekompose config file not found.", dekomposeConfigPath);
        }

        var dekomposeConfig = LoadDekomposeConfiguration(dekomposeConfigPath);
        lock (Sync)
        {
            var config = Load();
            config.Dekompose ??= new DekomposeConfiguration();
            config.Dekompose.CustomRules ??= new List<DekomposeRuleSet>();
            dekomposeConfig.CustomRules ??= new List<DekomposeRuleSet>();
            MergeDekomposeRules(config.Dekompose.CustomRules, dekomposeConfig.CustomRules);
            Save(config);
        }
    }

    private static DekomposeConfiguration LoadDekomposeConfiguration(string dekomposeConfigPath)
    {
        var yaml = File.ReadAllText(dekomposeConfigPath);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new DekomposeConfiguration();
        }

        DekomposeConfiguration? dekomposeConfig = null;
        try
        {
            var wrapper = Deserializer.Deserialize<DekomposeConfigWrapper>(yaml);
            dekomposeConfig = wrapper?.Dekompose;
        }
        catch
        {
            // Fall back to direct dekompose config deserialization below.
        }

        if (dekomposeConfig is null)
        {
            try
            {
                dekomposeConfig = Deserializer.Deserialize<DekomposeConfiguration>(yaml);
            }
            catch (Exception ex)
            {
                var message =
                    $"Failed to load Dekompose configuration at '{dekomposeConfigPath}'. " +
                    "Ensure the YAML is valid and includes a 'dekompose' section or dekompose-specific keys.";
                throw new InvalidOperationException(message, ex);
            }
        }

        return dekomposeConfig ?? new DekomposeConfiguration();
    }

    private static void MergeDekomposeRules(List<DekomposeRuleSet> destination, List<DekomposeRuleSet> incoming)
    {
        if (incoming is null || incoming.Count == 0)
        {
            return;
        }

        destination ??= new List<DekomposeRuleSet>();
        foreach (var rule in incoming)
        {
            if (rule is null)
            {
                continue;
            }

            var match = string.IsNullOrWhiteSpace(rule.Match) ? "*" : rule.Match;
            var index = destination.FindIndex(existing =>
                string.Equals(existing.Match ?? "*", match, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                destination[index] = rule;
            }
            else
            {
                destination.Add(rule);
            }
        }
    }

    private static string LocateConfigPath()
    {
        foreach (var fileName in new[] { "crosspose.yml", "crosspose.yaml" })
        {
            var path = ConfigFileLocator.GetConfigPath(fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return ConfigFileLocator.GetConfigPath("crosspose.yml");
    }

}

internal sealed class DekomposeConfigWrapper
{
    [YamlMember(Alias = "dekompose")]
    public DekomposeConfiguration? Dekompose { get; set; }
}
