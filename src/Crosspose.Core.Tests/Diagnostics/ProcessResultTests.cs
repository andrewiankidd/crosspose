using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Tests.Diagnostics;

public class ProcessResultTests
{
    [Fact]
    public void IsSuccess_ZeroExitCode_ReturnsTrue()
    {
        var result = new ProcessResult(0, "output", "");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void IsSuccess_NonZeroExitCode_ReturnsFalse()
    {
        var result = new ProcessResult(1, "", "error");
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(127)]
    [InlineData(255)]
    public void IsSuccess_VariousNonZeroCodes_ReturnsFalse(int exitCode)
    {
        var result = new ProcessResult(exitCode, "", "");
        Assert.False(result.IsSuccess);
    }
}
