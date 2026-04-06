using Crosspose.Doctor;
using Crosspose.Doctor.Checks;

namespace Crosspose.Doctor.Tests;

public class CheckCatalogTests
{
    [Fact]
    public void LoadAll_NoAdditionals_ReturnsBuiltInChecks()
    {
        var checks = CheckCatalog.LoadAll();

        Assert.NotEmpty(checks);
        // Built-in checks should always be present
        Assert.Contains(checks, c => c.Name == "docker-compose");
        Assert.Contains(checks, c => c.Name == "wsl");
        Assert.Contains(checks, c => c.Name == "helm");
    }

    [Fact]
    public void LoadAll_NoAdditionals_ExcludesAdditionalChecks()
    {
        var checks = CheckCatalog.LoadAll();

        // Additional checks should NOT be present without being enabled
        Assert.DoesNotContain(checks, c => c.IsAdditional);
    }

    [Fact]
    public void LoadAll_WithAzureAcrAuthWinKey_AddsWindowsAcrCheck()
    {
        var checks = CheckCatalog.LoadAll(new[] { "azure-acr-auth-win:myregistry.azurecr.io" });

        Assert.Contains(checks, c => c.Name.Contains("acr", StringComparison.OrdinalIgnoreCase)
                                      && c.Name.Contains("win", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadAll_WithAzureAcrAuthLinKey_AddsLinuxAcrCheck()
    {
        var checks = CheckCatalog.LoadAll(new[] { "azure-acr-auth-lin:myregistry.azurecr.io" });

        Assert.Contains(checks, c => c.Name.Contains("acr", StringComparison.OrdinalIgnoreCase)
                                      && c.Name.Contains("lin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadAll_WithLegacyAcrKey_CreatesCheckButFilteredWithoutWinKey()
    {
        // Legacy "azure-acr-auth:" creates AzureAcrAuthWinCheck but its AdditionalKey
        // is "azure-acr-auth-win:..." which isn't in the enabled set, so it gets filtered.
        // This is a known quirk — use the explicit key for reliable behavior.
        var checks = CheckCatalog.LoadAll(new[] { "azure-acr-auth:myregistry.azurecr.io" });

        // The legacy key alone doesn't survive the filter
        Assert.DoesNotContain(checks, c => c.Name.Contains("acr", StringComparison.OrdinalIgnoreCase));

        // But providing both keys works
        var checksWithBoth = CheckCatalog.LoadAll(new[]
        {
            "azure-acr-auth:myregistry.azurecr.io",
            "azure-acr-auth-win:myregistry.azurecr.io"
        });
        Assert.Contains(checksWithBoth, c => c.Name.Contains("acr", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadAll_WithPortProxyKey_AddsPortProxyCheck()
    {
        var checks = CheckCatalog.LoadAll(new[] { "port-proxy:1433@nat" });

        Assert.Contains(checks, c => c.Name.Contains("port-proxy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadAll_WithPortProxyArrowKey_AddsPortProxyCheck()
    {
        var checks = CheckCatalog.LoadAll(new[] { "port-proxy:1433>41433@nat" });

        Assert.Contains(checks, c => c.Name == "port-proxy-1433");
    }

    [Fact]
    public void LoadAll_WithMultipleKeys_AddsAllMatchingChecks()
    {
        var keys = new[]
        {
            "azure-acr-auth-win:reg1.azurecr.io",
            "azure-acr-auth-lin:reg1.azurecr.io",
            "port-proxy:1433@nat",
            "port-proxy:5432@nat"
        };
        var checks = CheckCatalog.LoadAll(keys);

        // Should have built-in + 4 additional
        var additionals = checks.Where(c => c.IsAdditional).ToList();
        Assert.Equal(4, additionals.Count);
    }

    [Fact]
    public void LoadAll_ContainsWslNetworkingModeCheck()
    {
        var checks = CheckCatalog.LoadAll();

        Assert.Contains(checks, c => c.Name == "wsl-networking-mode");
    }

    [Fact]
    public void LoadAll_WslNetworkingModeBeforePortProxy()
    {
        var checks = CheckCatalog.LoadAll(new[] { "port-proxy:1433@nat" }).ToList();

        var netIdx = checks.FindIndex(c => c.Name == "wsl-networking-mode");
        var ppIdx = checks.FindIndex(c => c.Name == "port-proxy-1433");
        Assert.True(netIdx >= 0, "wsl-networking-mode should be present");
        Assert.True(ppIdx >= 0, "port-proxy-1433 should be present");
        Assert.True(netIdx < ppIdx, "wsl-networking-mode should run before port-proxy checks");
    }

    [Fact]
    public void LoadAll_EmptyKey_Ignored()
    {
        var checks = CheckCatalog.LoadAll(new[] { "", "  " });

        // Should just return built-in checks, no crash
        Assert.NotEmpty(checks);
        Assert.DoesNotContain(checks, c => c.IsAdditional);
    }

    [Fact]
    public void LoadAll_AllBuiltInChecksHaveRequiredProperties()
    {
        var checks = CheckCatalog.LoadAll();

        foreach (var check in checks)
        {
            Assert.False(string.IsNullOrWhiteSpace(check.Name), $"Check has empty Name");
            Assert.False(string.IsNullOrWhiteSpace(check.Description), $"Check '{check.Name}' has empty Description");
        }
    }

    [Fact]
    public void LoadAll_ContainsOrphanedDockerNetworkCheck()
    {
        var checks = CheckCatalog.LoadAll();

        Assert.Contains(checks, c => c.Name == "orphaned-docker-networks");
    }

    [Fact]
    public void LoadAll_ContainsStalePortProxyCheck()
    {
        var checks = CheckCatalog.LoadAll();

        Assert.Contains(checks, c => c.Name == "stale-port-proxies");
    }

    [Fact]
    public void LoadAll_ContainsStalePortProxyConfigCheck()
    {
        var checks = CheckCatalog.LoadAll();

        Assert.Contains(checks, c => c.Name == "stale-port-proxy-config");
    }

    [Fact]
    public void LoadAll_ContainsHnsNatHealthCheck()
    {
        var checks = CheckCatalog.LoadAll();

        Assert.Contains(checks, c => c.Name == "hns-nat-health");
    }

    [Fact]
    public void LoadAll_BuiltInChecksInExpectedOrder()
    {
        var checks = CheckCatalog.LoadAll().ToList();

        var dockerIdx = checks.FindIndex(c => c.Name == "docker-compose");
        var hnsIdx = checks.FindIndex(c => c.Name == "hns-nat-health");
        var orphanNetIdx = checks.FindIndex(c => c.Name == "orphaned-docker-networks");
        var staleConfigIdx = checks.FindIndex(c => c.Name == "stale-port-proxy-config");
        var wslIdx = checks.FindIndex(c => c.Name == "wsl");
        var netModeIdx = checks.FindIndex(c => c.Name == "wsl-networking-mode");
        var staleProxyIdx = checks.FindIndex(c => c.Name == "stale-port-proxies");

        // HNS must be healthy before we try to create/remove Docker networks
        Assert.True(dockerIdx < hnsIdx, "docker-compose should be checked before hns-nat-health");
        Assert.True(hnsIdx < orphanNetIdx, "hns-nat-health should be checked before orphaned-docker-networks");
        // Config cleanup runs after network cleanup so the network list is accurate
        Assert.True(orphanNetIdx < staleConfigIdx, "orphaned-docker-networks should be checked before stale-port-proxy-config");
        // Docker checks before WSL checks
        Assert.True(staleConfigIdx < wslIdx, "stale-port-proxy-config should be checked before wsl");
        // WSL checks before stale portproxy rules
        Assert.True(wslIdx < netModeIdx, "wsl should be checked before wsl-networking-mode");
        Assert.True(netModeIdx < staleProxyIdx, "wsl-networking-mode should be checked before stale-port-proxies");
    }
}
