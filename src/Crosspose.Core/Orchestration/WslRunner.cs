using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Orchestration;

public sealed class WslRunner : VirtualizationPlatformRunnerBase
{
    public WslRunner(ProcessRunner runner) : base("wsl", runner)
    {
    }
}
