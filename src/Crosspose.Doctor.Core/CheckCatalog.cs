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
            // --- Tool installation (winget only, no other dependencies) ---
            new DockerComposeCheck(),
            new HelmCheck(),
            new AzureCliCheck(),

            // --- Docker setup ---
            new DockerUsersGroupCheck(),
            new WindowsContainersFeatureCheck(),
            new DockerHelperServiceCheck(),
            new DockerRunningCheck(),
            new DockerWindowsModeCheck(),
            new HnsNatHealthCheck(),
            new OrphanedDockerNetworkCheck(),

            // Config cleanup runs after network cleanup so the network list is accurate,
            // but before WSL so Docker state is settled first.
            new StalePortProxyConfigCheck(),

            // --- WSL base ---
            new WslCheck(),
            new WslMemoryLimitCheck(),
            new WslNetworkingModeCheck(),

            // --- Crosspose WSL distro (all checks below run inside it) ---
            new CrossposeWslCheck(),
            new SudoCheck(),
            new PodmanWslCheck(),
            new PodmanCgroupCheck(),
            new PodmanComposeWslCheck(),
            new OrphanedPodmanNetworkCheck(),

            // --- Port proxy / firewall (StalePortProxyCheck queries listeners inside the distro) ---
            new StalePortProxyCheck(),
            new StaleFirewallRuleCheck(),

            // --- Podman container state ---
            new PodmanHealthcheckRunnerCheck(),
            new PodmanCreatedContainerCheck(),
            new PodmanContainerAutohealCheck(),

            // --- Networking ---
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
