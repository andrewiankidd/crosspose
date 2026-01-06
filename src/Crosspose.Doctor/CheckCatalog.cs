using Crosspose.Core.Configuration;
using Crosspose.Doctor.Checks;

namespace Crosspose.Doctor;

public static class CheckCatalog
{
    public static IReadOnlyList<ICheckFix> LoadAll(IEnumerable<string>? enabledAdditionalKeys = null)
    {
        var enabled = new HashSet<string>(enabledAdditionalKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var checks = new List<ICheckFix>
        {
            new DockerComposeCheck(),
            new DockerRunningCheck(),
            new DockerWindowsModeCheck(),
            new WslCheck(),
            new WslMemoryLimitCheck(),
            new SudoCheck(),
            new CrossposeWslCheck(),
            new PodmanWslCheck(),
            new PodmanCgroupCheck(),
            new PodmanComposeWslCheck(),
            new HelmCheck(),
            new AzureCliCheck()
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
            else if (PortProxyKey.TryParse(key, out var port, out var network))
            {
                checks.Add(new PortProxyCheck(port, network));
            }
        }

        return checks
            .Where(c => !c.IsAdditional || enabled.Contains(c.AdditionalKey))
            .ToList();
    }
}
