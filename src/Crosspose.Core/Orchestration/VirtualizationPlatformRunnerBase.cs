using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Orchestration;

public abstract class VirtualizationPlatformRunnerBase : IVirtualizationPlatformRunner
{
    protected readonly ProcessRunner Runner;

    protected VirtualizationPlatformRunnerBase(string baseCommand, ProcessRunner runner)
    {
        BaseCommand = baseCommand;
        Runner = runner;
    }

    public string BaseCommand { get; }

    public virtual Task<ProcessResult> ExecAsync(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
    {
        var argumentString = string.Join(" ", args);
        return Runner.RunAsync(BaseCommand, argumentString, environment: environment, cancellationToken: cancellationToken);
    }
}
