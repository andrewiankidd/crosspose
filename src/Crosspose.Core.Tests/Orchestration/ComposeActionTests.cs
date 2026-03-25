using Crosspose.Core.Orchestration;

namespace Crosspose.Core.Tests.Orchestration;

public class ComposeActionTests
{
    [Theory]
    [InlineData("up", ComposeAction.Up)]
    [InlineData("down", ComposeAction.Down)]
    [InlineData("restart", ComposeAction.Restart)]
    [InlineData("stop", ComposeAction.Stop)]
    [InlineData("start", ComposeAction.Start)]
    [InlineData("ps", ComposeAction.Ps)]
    [InlineData("status", ComposeAction.Ps)]
    [InlineData("logs", ComposeAction.Logs)]
    [InlineData("top", ComposeAction.Top)]
    public void TryParse_ValidActions_ReturnsTrue(string input, ComposeAction expected)
    {
        Assert.True(ComposeActionExtensions.TryParse(input, out var action));
        Assert.Equal(expected, action);
    }

    [Theory]
    [InlineData("UP")]
    [InlineData("Down")]
    [InlineData("RESTART")]
    public void TryParse_CaseInsensitive(string input)
    {
        Assert.True(ComposeActionExtensions.TryParse(input, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("invalid")]
    [InlineData("deploy")]
    public void TryParse_InvalidActions_ReturnsFalse(string? input)
    {
        Assert.False(ComposeActionExtensions.TryParse(input, out _));
    }

    [Theory]
    [InlineData(ComposeAction.Up, "up")]
    [InlineData(ComposeAction.Down, "down")]
    [InlineData(ComposeAction.Restart, "restart")]
    [InlineData(ComposeAction.Stop, "stop")]
    [InlineData(ComposeAction.Start, "start")]
    [InlineData(ComposeAction.Ps, "ps")]
    [InlineData(ComposeAction.Logs, "logs")]
    [InlineData(ComposeAction.Top, "top")]
    public void ToCommand_ReturnsExpectedString(ComposeAction action, string expected)
    {
        Assert.Equal(expected, action.ToCommand());
    }
}
