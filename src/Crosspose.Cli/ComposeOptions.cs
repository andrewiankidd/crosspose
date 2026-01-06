using System.Collections.Generic;
using Crosspose.Core.Configuration;
using Crosspose.Core.Orchestration;

internal sealed class ComposeOptions
{
    public ComposeAction Action { get; set; } = ComposeAction.Up;
    public string Directory { get; set; } = CrossposeEnvironment.OutputDirectory;
    public string? Workload { get; set; }
    public bool Detached { get; set; }
    public string? ProjectName { get; set; }
    public List<string> ExtraArgs { get; } = new();
}
