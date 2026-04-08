using Crosspose.Core.Orchestration;

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

    // --- ApplyFilter: hello world filter specificity ---

    [Fact]
    public void ApplyFilter_HelloWorldFilter_MatchesHelloWorldChart()
    {
        var repos = new List<string> { "andrewiankidd/charts/cross-platform-hello" };
        var result = OciRegistryStore.ApplyFilter(repos, OciRegistryStore.HelloWorldDefault.Filter);
        Assert.Single(result);
        Assert.Equal("andrewiankidd/charts/cross-platform-hello", result[0]);
    }

    [Fact]
    public void ApplyFilter_HelloWorldFilter_ExcludesOtherChartsFromSameRegistry()
    {
        var repos = new List<string>
        {
            "andrewiankidd/charts/cross-platform-hello",
            "andrewiankidd/charts/some-other-chart",
            "andrewiankidd/charts/another-chart",
        };
        var result = OciRegistryStore.ApplyFilter(repos, OciRegistryStore.HelloWorldDefault.Filter);
        Assert.Single(result);
        Assert.Equal("andrewiankidd/charts/cross-platform-hello", result[0]);
    }

    [Fact]
    public void ApplyFilter_HelloWorldFilter_ExcludesUnrelatedRegistries()
    {
        var repos = new List<string>
        {
            "someoneelse/charts/cross-platform-hello",
            "andrewiankidd/charts/cross-platform-hello",
        };
        var result = OciRegistryStore.ApplyFilter(repos, OciRegistryStore.HelloWorldDefault.Filter);
        Assert.Single(result);
        Assert.Equal("andrewiankidd/charts/cross-platform-hello", result[0]);
    }

    // --- ApplyFilter: general behaviour ---

    [Fact]
    public void ApplyFilter_NullFilter_ReturnsAll()
    {
        var repos = new List<string> { "a/b", "c/d" };
        var result = OciRegistryStore.ApplyFilter(repos, null);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ApplyFilter_EmptySource_ReturnsEmpty()
    {
        var result = OciRegistryStore.ApplyFilter(new List<string>(), "anything");
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyFilter_IsCaseInsensitive()
    {
        var repos = new List<string> { "Andrewiankidd/Charts/Cross-Platform-Hello" };
        var result = OciRegistryStore.ApplyFilter(repos, OciRegistryStore.HelloWorldDefault.Filter);
        Assert.Single(result);
    }
}
