using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Core.Tests.Orchestration;

public class OciRegistryStoreTests
{
    // --- HelloWorldDefault shape ---

    [Fact]
    public void HelloWorldDefault_PointsToGhcr()
    {
        Assert.Equal("https://ghcr.io", OciRegistryStore.HelloWorldDefault.Address);
    }

    [Fact]
    public void HelloWorldDefault_FilterIsFullChartPath()
    {
        Assert.Equal("andrewiankidd/charts/cross-platform-hello", OciRegistryStore.HelloWorldDefault.Filter);
    }

    [Fact]
    public void HelloWorldDefault_HasNoCredentials()
    {
        Assert.Null(OciRegistryStore.HelloWorldDefault.Username);
        Assert.Null(OciRegistryStore.HelloWorldDefault.Password);
        Assert.Null(OciRegistryStore.HelloWorldDefault.BearerToken);
    }

    // --- ListChartsAsync: exact-filter shortcut (no catalog call) ---

    [Fact]
    public async Task ListChartsAsync_ExactFilter_ReturnsFilterDirectly()
    {
        var entry = new OciRegistryEntry
        {
            Name = "test",
            Address = "https://ghcr.io",
            Filter = "andrewiankidd/charts/cross-platform-hello"
        };

        var store = new OciRegistryStore(NullLogger.Instance);
        var result = await store.ListChartsAsync(entry);

        Assert.Single(result);
        Assert.Equal("andrewiankidd/charts/cross-platform-hello", result[0]);
    }

    [Fact]
    public async Task ListChartsAsync_WildcardFilter_HitsCatalog()
    {
        // A wildcard filter goes through the catalog — not short-circuited.
        var entry = new OciRegistryEntry
        {
            Name = "test",
            Address = "https://ghcr.io",
            Filter = "andrewiankidd/charts/*"
        };

        var store = new OciRegistryStore(NullLogger.Instance);
        var result = await store.ListChartsAsync(entry);

        Assert.DoesNotContain("andrewiankidd/charts/*", result);
    }
}
