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
    public void LoadAll_BuiltInChecksInExpectedOrder()
    {
        var checks = CheckCatalog.LoadAll();

        // DockerCompose should come before WSL (Docker needs to be checked first)
        var dockerIdx = checks.ToList().FindIndex(c => c.Name == "docker-compose");
        var wslIdx = checks.ToList().FindIndex(c => c.Name == "wsl");
        Assert.True(dockerIdx < wslIdx, "docker-compose should be checked before wsl");
    }
}
