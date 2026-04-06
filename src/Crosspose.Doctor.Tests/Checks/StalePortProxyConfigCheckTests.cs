using Crosspose.Doctor.Checks;

namespace Crosspose.Doctor.Tests.Checks;

public class StalePortProxyConfigCheckTests
{
    [Fact]
    public void Check_HasCorrectMetadata()
    {
        var check = new StalePortProxyConfigCheck();

        Assert.Equal("stale-port-proxy-config", check.Name);
        Assert.False(check.IsAdditional);
        Assert.True(check.CanFix);
        Assert.False(string.IsNullOrWhiteSpace(check.Description));
    }
}
