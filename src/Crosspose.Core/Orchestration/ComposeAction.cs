namespace Crosspose.Core.Orchestration;

public enum ComposeAction
{
    Up,
    Down,
    Restart,
    Stop,
    Start,
    Ps,
    Logs,
    Top
}

public static class ComposeActionExtensions
{
    public static bool TryParse(string? value, out ComposeAction action)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            action = ComposeAction.Up;
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "up":
                action = ComposeAction.Up;
                return true;
            case "down":
                action = ComposeAction.Down;
                return true;
            case "restart":
                action = ComposeAction.Restart;
                return true;
            case "stop":
                action = ComposeAction.Stop;
                return true;
            case "start":
                action = ComposeAction.Start;
                return true;
            case "ps":
            case "status":
                action = ComposeAction.Ps;
                return true;
            case "logs":
                action = ComposeAction.Logs;
                return true;
            case "top":
                action = ComposeAction.Top;
                return true;
            default:
                action = ComposeAction.Up;
                return false;
        }
    }

    public static string ToCommand(this ComposeAction action) =>
        action switch
        {
            ComposeAction.Up => "up",
            ComposeAction.Down => "down",
            ComposeAction.Restart => "restart",
            ComposeAction.Stop => "stop",
            ComposeAction.Start => "start",
            ComposeAction.Ps => "ps",
            ComposeAction.Logs => "logs",
            ComposeAction.Top => "top",
            _ => "up"
        };
}
