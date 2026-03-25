using Crosspose.Core.Configuration;

namespace Crosspose.Core.Tests.Configuration;

public class PortProxyKeyTests
{
    [Fact]
    public void Format_PortOnly_ReturnsCorrectKey()
    {
        Assert.Equal("port-proxy:8080", PortProxyKey.Format(8080, null));
    }

    [Fact]
    public void Format_PortAndNetwork_ReturnsCorrectKey()
    {
        Assert.Equal("port-proxy:1433@nat", PortProxyKey.Format(1433, "nat"));
    }

    [Fact]
    public void Format_InvalidPort_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PortProxyKey.Format(0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortProxyKey.Format(-1, "net"));
    }

    [Fact]
    public void TryParse_PortOnly_Succeeds()
    {
        Assert.True(PortProxyKey.TryParse("port-proxy:8080", out var port, out var network));
        Assert.Equal(8080, port);
        Assert.Null(network);
    }

    [Fact]
    public void TryParse_PortAndNetwork_Succeeds()
    {
        Assert.True(PortProxyKey.TryParse("port-proxy:1433@mynet", out var port, out var network));
        Assert.Equal(1433, port);
        Assert.Equal("mynet", network);
    }

    [Fact]
    public void TryParse_CaseInsensitive()
    {
        Assert.True(PortProxyKey.TryParse("Port-Proxy:9090", out var port, out _));
        Assert.Equal(9090, port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-a-port-proxy")]
    [InlineData("port-proxy:")]
    [InlineData("port-proxy:abc")]
    [InlineData("port-proxy:-1")]
    [InlineData("port-proxy:0")]
    public void TryParse_InvalidInputs_ReturnsFalse(string? input)
    {
        Assert.False(PortProxyKey.TryParse(input!, out _, out _));
    }

    [Fact]
    public void Roundtrip_FormatThenParse()
    {
        var key = PortProxyKey.Format(5432, "bridged");
        Assert.True(PortProxyKey.TryParse(key, out var port, out var network));
        Assert.Equal(5432, port);
        Assert.Equal("bridged", network);
    }
}
