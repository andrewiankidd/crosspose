namespace Crosspose.Core.Orchestration;

public sealed record ComposeExecutionRequest(
    string SourcePath,
    ComposeAction Action,
    string? Workload = null,
    bool Detached = false,
    string? ProjectName = null,
    IReadOnlyList<string>? AdditionalArguments = null);

public sealed record ComposeExecutionResult(PlatformCommandResult? DockerResult, PlatformCommandResult? PodmanResult)
{
    public bool HasAnyOutput => DockerResult is not null || PodmanResult is not null;

    /// <summary>
    /// True when portproxy/firewall rules need to be applied but the process is not elevated.
    /// The caller should re-run the fix with administrator rights (e.g. launch Doctor as admin).
    /// </summary>
    public bool PortProxyFixRequired { get; init; }
}
