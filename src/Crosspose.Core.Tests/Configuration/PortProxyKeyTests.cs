using Crosspose.Core.Configuration;

namespace Crosspose.Core.Tests.Configuration;

public class PortProxyKeyTests
{
    [Fact]
    public void Format_PortOnly_SameListenAndConnect_ReturnsSimpleKey()
    {
        Assert.Equal("port-proxy:8080", PortProxyKey.Format(8080, null));
    }

    [Fact]
    public void Format_PortAndNetwork_SamePorts_ReturnsKeyWithNetwork()
    {
        Assert.Equal("port-proxy:1433@nat", PortProxyKey.Format(1433, "nat"));
    }

    [Fact]
    public void Format_DifferentListenAndConnect_EncodesArrow()
    {
        Assert.Equal("port-proxy:1433>41234", PortProxyKey.Format(1433, 41234, null));
    }

    [Fact]
    public void Format_DifferentPortsAndNetwork_EncodesAll()
    {
        Assert.Equal("port-proxy:1433>41234@mynet", PortProxyKey.Format(1433, 41234, "mynet"));
    }

    [Fact]
    public void Format_SameListenAndConnect_OmitsArrow()
    {
        Assert.Equal("port-proxy:1433@mynet", PortProxyKey.Format(1433, 1433, "mynet"));
    }

    [Fact]
    public void Format_InvalidPort_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PortProxyKey.Format(0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortProxyKey.Format(-1, "net"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortProxyKey.Format(1433, 0, null));
    }

    [Fact]
    public void TryParse_PortOnly_Succeeds_ConnectPortEqualsListen()
    {
        Assert.True(PortProxyKey.TryParse("port-proxy:8080", out var listen, out var connect, out var network));
        Assert.Equal(8080, listen);
        Assert.Equal(8080, connect);
        Assert.Null(network);
    }

    [Fact]
    public void TryParse_PortAndNetwork_Succeeds()
    {
        Assert.True(PortProxyKey.TryParse("port-proxy:1433@mynet", out var listen, out var connect, out var network));
        Assert.Equal(1433, listen);
        Assert.Equal(1433, connect);
        Assert.Equal("mynet", network);
    }

    [Fact]
    public void TryParse_ArrowFormat_DecodesCorrectly()
    {
        Assert.True(PortProxyKey.TryParse("port-proxy:1433>41234@mynet", out var listen, out var connect, out var network));
        Assert.Equal(1433, listen);
        Assert.Equal(41234, connect);
        Assert.Equal("mynet", network);
    }

    [Fact]
    public void TryParse_ArrowNoNetwork_DecodesCorrectly()
    {
        Assert.True(PortProxyKey.TryParse("port-proxy:5672>45672", out var listen, out var connect, out var network));
        Assert.Equal(5672, listen);
        Assert.Equal(45672, connect);
        Assert.Null(network);
    }

    [Fact]
    public void TryParse_CaseInsensitive()
    {
        Assert.True(PortProxyKey.TryParse("Port-Proxy:9090", out var port, out _, out _));
        Assert.Equal(9090, port);
    }

    [Fact]
    public void TryParse_BackwardCompatOverload_IgnoresConnectPort()
    {
        Assert.True(PortProxyKey.TryParse("port-proxy:1433>41234@net", out var port, out var network));
        Assert.Equal(1433, port);
        Assert.Equal("net", network);
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
    [InlineData("port-proxy:1433>0")]
    [InlineData("port-proxy:1433>abc")]
    public void TryParse_InvalidInputs_ReturnsFalse(string? input)
    {
        Assert.False(PortProxyKey.TryParse(input!, out _, out _, out _));
    }

    [Fact]
    public void Roundtrip_SamePorts()
    {
        var key = PortProxyKey.Format(5432, "bridged");
        Assert.True(PortProxyKey.TryParse(key, out var listen, out var connect, out var network));
        Assert.Equal(5432, listen);
        Assert.Equal(5432, connect);
        Assert.Equal("bridged", network);
    }

    [Fact]
    public void Roundtrip_DifferentPorts()
    {
        var key = PortProxyKey.Format(1433, 41433, "mynet");
        Assert.True(PortProxyKey.TryParse(key, out var listen, out var connect, out var network));
        Assert.Equal(1433, listen);
        Assert.Equal(41433, connect);
        Assert.Equal("mynet", network);
    }
}
