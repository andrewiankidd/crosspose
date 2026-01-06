namespace Crosspose.Core.Diagnostics;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
