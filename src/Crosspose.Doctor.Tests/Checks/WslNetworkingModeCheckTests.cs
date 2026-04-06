using Crosspose.Doctor.Core.Checks;

namespace Crosspose.Doctor.Tests.Checks;

public class WslNetworkingModeCheckTests
{
    [Fact]
    public void Check_HasCorrectMetadata()
    {
        var check = new WslNetworkingModeCheck();

        Assert.Equal("wsl-networking-mode", check.Name);
        Assert.False(check.IsAdditional);
        Assert.True(check.CanFix);
        Assert.False(string.IsNullOrWhiteSpace(check.Description));
    }
}
