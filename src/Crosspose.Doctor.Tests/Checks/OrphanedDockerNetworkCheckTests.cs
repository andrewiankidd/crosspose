using Crosspose.Doctor.Core.Checks;

namespace Crosspose.Doctor.Tests.Checks;

public class OrphanedDockerNetworkCheckTests
{
    [Fact]
    public void Check_HasCorrectMetadata()
    {
        var check = new OrphanedDockerNetworkCheck();

        Assert.Equal("orphaned-docker-networks", check.Name);
        Assert.False(check.IsAdditional);
        Assert.True(check.CanFix);
        Assert.False(string.IsNullOrWhiteSpace(check.Description));
    }
}
