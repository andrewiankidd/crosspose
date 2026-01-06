using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Crosspose.Core.Configuration;

/// <summary>
/// Centralized access to environment-derived configuration for Crosspose.
/// </summary>
public static class CrossposeEnvironment
{
    public const string DefaultOutputDirectory = "dekompose-outputs";
    public const string DefaultWslDistro = "crosspose-data";
    public const string DefaultWslUser = "crossposeuser";
    public const string DefaultWslPassword = "crossposepassword";
    public const int DefaultGuiRefreshIntervalSeconds = 5;

    private static readonly Lazy<CrossposeConfiguration> Configuration =
        new(() => CrossposeConfigurationStore.Load());

    public static string OutputDirectory
    {
        get
        {
            var configured = GetConfigValue(cfg => cfg.Compose.OutputDirectory, DefaultOutputDirectory);
            return AppDataLocator.GetPreferredDirectory(configured);
        }
    }

    public static string DeploymentDirectory
    {
        get
        {
            var configured = GetConfigValue(cfg => cfg.Compose.DeploymentDirectory, "crosspose-deployments");
            return AppDataLocator.GetPreferredDirectory(configured);
        }
    }

    public static string? LogFilePath
    {
        get
        {
            var configured = GetOptionalConfigValue(cfg => cfg.Compose.LogFile);
            if (string.IsNullOrWhiteSpace(configured)) return null;
            return AppDataLocator.GetPreferredFilePath(configured);
        }
    }

    public static int GuiRefreshIntervalSeconds => GetConfigInt(cfg => cfg.Compose.Gui.RefreshIntervalSeconds, DefaultGuiRefreshIntervalSeconds);

    public static string WslDistro => GetConfigValue(cfg => cfg.Compose.Wsl.Distro, DefaultWslDistro);
    public static string WslUser => GetConfigValue(cfg => cfg.Compose.Wsl.User, DefaultWslUser);
    public static string WslPassword => GetConfigValue(cfg => cfg.Compose.Wsl.Password, DefaultWslPassword);
    public static string Path => Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    public static bool HasCommandPrompt => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROMPT"));
    public static bool HasPowerShellModulePath => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PSModulePath"));
    public static bool IsShellAvailable => HasCommandPrompt || HasPowerShellModulePath;

    public static IReadOnlyList<DekomposeRuleSet> GetDekomposeRules(string? chartName)
    {
        if (string.IsNullOrWhiteSpace(chartName))
        {
            return Array.Empty<DekomposeRuleSet>();
        }

        var rules = Configuration.Value.Dekompose?.CustomRules;
        if (rules is null || rules.Count == 0)
        {
            return Array.Empty<DekomposeRuleSet>();
        }

        var matches = new List<DekomposeRuleSet>();
        foreach (var rule in rules)
        {
            if (IsPatternMatch(rule.Match, chartName))
            {
                matches.Add(rule);
            }
        }

        return matches;
    }

    private static string GetConfigValue(Func<CrossposeConfiguration, string?> accessor, string fallback)
    {
        var configValue = NormalizeConfigValue(accessor(Configuration.Value));
        return configValue ?? fallback;
    }

    private static string? GetOptionalConfigValue(Func<CrossposeConfiguration, string?> accessor) =>
        NormalizeConfigValue(accessor(Configuration.Value));

    private static int GetConfigInt(Func<CrossposeConfiguration, int?> accessor, int fallback)
    {
        var configValue = accessor(Configuration.Value);
        return (configValue.HasValue && configValue.Value > 0) ? configValue.Value : fallback;
    }

    private static string? NormalizeConfigValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool IsPatternMatch(string? pattern, string chartName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(chartName, regex, RegexOptions.IgnoreCase);
    }

}
