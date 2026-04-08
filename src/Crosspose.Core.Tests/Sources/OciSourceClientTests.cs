using Crosspose.Core.Sources;

namespace Crosspose.Core.Tests.Sources;

public class OciSourceClientTests
{
    // --- ApplyFilter ---

    [Fact]
    public void ApplyFilter_HelloWorldFilter_MatchesHelloWorldChart()
    {
        var repos = new[] { "andrewiankidd/charts/cross-platform-hello" };
        var result = OciSourceClient.ApplyFilter(repos, "andrewiankidd/charts/cross-platform-hello");
        Assert.Single(result);
        Assert.Equal("andrewiankidd/charts/cross-platform-hello", result[0]);
    }

    [Fact]
    public void ApplyFilter_HelloWorldFilter_ExcludesOtherChartsFromSameRegistry()
    {
        var repos = new[]
        {
            "andrewiankidd/charts/cross-platform-hello",
            "andrewiankidd/charts/some-other-chart",
            "andrewiankidd/charts/another-chart",
        };
        var result = OciSourceClient.ApplyFilter(repos, "andrewiankidd/charts/cross-platform-hello");
        Assert.Single(result);
        Assert.Equal("andrewiankidd/charts/cross-platform-hello", result[0]);
    }

    [Fact]
    public void ApplyFilter_NullFilter_ReturnsAll()
    {
        var repos = new[] { "a/b", "c/d" };
        var result = OciSourceClient.ApplyFilter(repos, null);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ApplyFilter_EmptySource_ReturnsEmpty()
    {
        var result = OciSourceClient.ApplyFilter(Array.Empty<string>(), "anything");
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyFilter_IsCaseInsensitive()
    {
        var repos = new[] { "Andrewiankidd/Charts/Cross-Platform-Hello" };
        var result = OciSourceClient.ApplyFilter(repos, "andrewiankidd/charts/cross-platform-hello");
        Assert.Single(result);
    }

    // --- ExtractWwwAuthParam ---

    // Typical GHCR challenge:
    // Bearer realm="https://ghcr.io/token",service="ghcr.io",scope="registry:catalog:*"

    [Fact]
    public void ExtractWwwAuthParam_Realm_ParsedCorrectly()
    {
        const string param = "realm=\"https://ghcr.io/token\",service=\"ghcr.io\",scope=\"registry:catalog:*\"";
        Assert.Equal("https://ghcr.io/token", OciSourceClient.ExtractWwwAuthParam(param, "realm"));
    }

    [Fact]
    public void ExtractWwwAuthParam_Service_ParsedCorrectly()
    {
        const string param = "realm=\"https://ghcr.io/token\",service=\"ghcr.io\",scope=\"registry:catalog:*\"";
        Assert.Equal("ghcr.io", OciSourceClient.ExtractWwwAuthParam(param, "service"));
    }

    [Fact]
    public void ExtractWwwAuthParam_Scope_ParsedCorrectly()
    {
        const string param = "realm=\"https://ghcr.io/token\",service=\"ghcr.io\",scope=\"registry:catalog:*\"";
        Assert.Equal("registry:catalog:*", OciSourceClient.ExtractWwwAuthParam(param, "scope"));
    }

    [Fact]
    public void ExtractWwwAuthParam_MissingKey_ReturnsNull()
    {
        const string param = "realm=\"https://ghcr.io/token\",service=\"ghcr.io\"";
        Assert.Null(OciSourceClient.ExtractWwwAuthParam(param, "scope"));
    }

    [Fact]
    public void ExtractWwwAuthParam_IsCaseInsensitive()
    {
        const string param = "Realm=\"https://ghcr.io/token\"";
        Assert.Equal("https://ghcr.io/token", OciSourceClient.ExtractWwwAuthParam(param, "realm"));
    }

    [Fact]
    public void ExtractWwwAuthParam_EmptyParam_ReturnsNull()
    {
        Assert.Null(OciSourceClient.ExtractWwwAuthParam("", "realm"));
    }

    [Fact]
    public void ExtractWwwAuthParam_RealmOnly_ParsedCorrectly()
    {
        const string param = "realm=\"https://example.com/token\"";
        Assert.Equal("https://example.com/token", OciSourceClient.ExtractWwwAuthParam(param, "realm"));
        Assert.Null(OciSourceClient.ExtractWwwAuthParam(param, "service"));
    }
}
