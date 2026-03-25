using Crosspose.Doctor.Checks;

namespace Crosspose.Doctor.Tests.Checks;

public class CheckResultTests
{
    [Fact]
    public void Success_ReturnsIsSuccessfulTrue()
    {
        var result = CheckResult.Success("all good");
        Assert.True(result.IsSuccessful);
        Assert.Equal("all good", result.Message);
    }

    [Fact]
    public void Failure_ReturnsIsSuccessfulFalse()
    {
        var result = CheckResult.Failure("not found");
        Assert.False(result.IsSuccessful);
        Assert.Equal("not found", result.Message);
    }
}

public class FixResultTests
{
    [Fact]
    public void Success_ReturnsSucceededTrue()
    {
        var result = FixResult.Success("installed");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Failure_ReturnsSucceededFalse()
    {
        var result = FixResult.Failure("winget not available");
        Assert.False(result.Succeeded);
    }
}
