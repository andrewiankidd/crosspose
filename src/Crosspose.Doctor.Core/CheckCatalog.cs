using Crosspose.Core.Configuration;
using Crosspose.Doctor.Core.Checks;

namespace Crosspose.Doctor.Core;

public static class CheckCatalog
{
    public static IReadOnlyList<ICheckFix> LoadAll(
        IEnumerable<string>? enabledAdditionalKeys = null,
        bool offlineMode = false)
    {
        var enabled = new HashSet<string>(enabledAdditionalKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var checks = new List<ICheckFix>
        {
            new DockerComposeCheck(),
            new DockerRunningCheck(),
            new DockerWindowsModeCheck(),
            new HnsNatHealthCheck(),
            new OrphanedDockerNetworkCheck(),
            new OrphanedPodmanNetworkCheck(),
            new StalePortProxyConfigCheck(),
            new WslCheck(),
            new WslMemoryLimitCheck(),
            new WslNetworkingModeCheck(),
            new StalePortProxyCheck(),
            new StaleFirewallRuleCheck(),
            new SudoCheck(),
            new CrossposeWslCheck(),
            new PodmanWslCheck(),
            new PodmanCgroupCheck(),
            new PodmanComposeWslCheck(),
            new HelmCheck(),
            new AzureCliCheck(),
            new PodmanHealthcheckRunnerCheck(),
            new PodmanCreatedContainerCheck(),
            new PodmanContainerAutohealCheck(),
            new WslToWindowsFirewallCheck()
        };

        foreach (var key in enabled)
        {
            if (key.StartsWith("azure-acr-auth-win:", StringComparison.OrdinalIgnoreCase))
            {
                var registry = key["azure-acr-auth-win:".Length..];
                if (!string.IsNullOrWhiteSpace(registry))
                {
                    checks.Add(new AzureAcrAuthWinCheck(registry));
                }
            }
            else if (key.StartsWith("azure-acr-auth-lin:", StringComparison.OrdinalIgnoreCase))
            {
                var registry = key["azure-acr-auth-lin:".Length..];
                if (!string.IsNullOrWhiteSpace(registry))
                {
                    checks.Add(new AzureAcrAuthLinCheck(registry));
                }
            }
            else if (key.StartsWith("azure-acr-auth:", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy key - treat as Windows auth.
                var registry = key["azure-acr-auth:".Length..];
                if (!string.IsNullOrWhiteSpace(registry))
                {
                    checks.Add(new AzureAcrAuthWinCheck(registry));
                }
            }
            else if (PortProxyKey.TryParse(key, out var listenPort, out var connectPort, out var network))
            {
                checks.Add(new PortProxyCheck(listenPort, connectPort, network));
            }
        }

        return checks
            .Where(c => !c.IsAdditional || enabled.Contains(c.AdditionalKey))
            .Where(c => !offlineMode || !c.RequiresConnectivity)
            .ToList();
    }
}
