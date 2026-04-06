using Crosspose.Doctor.Core.Checks;

namespace Crosspose.Doctor.Tests.Checks;

public class StalePortProxyCheckTests
{
    [Fact]
    public void Check_HasCorrectMetadata()
    {
        var check = new StalePortProxyCheck();

        Assert.Equal("stale-port-proxies", check.Name);
        Assert.False(check.IsAdditional);
        Assert.True(check.CanFix);
        Assert.False(string.IsNullOrWhiteSpace(check.Description));
    }
}
