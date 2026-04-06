using Crosspose.Doctor.Checks;

namespace Crosspose.Doctor.Tests.Checks;

public class HnsNatHealthCheckTests
{
    [Fact]
    public void Check_HasCorrectMetadata()
    {
        var check = new HnsNatHealthCheck();

        Assert.Equal("hns-nat-health", check.Name);
        Assert.False(check.IsAdditional);
        Assert.True(check.CanFix);
        Assert.False(string.IsNullOrWhiteSpace(check.Description));
    }
}
