using System;

namespace Crosspose.Core.Deployment;

public sealed class DeploymentMetadata
{
    public string? Project { get; set; }
    public string? Version { get; set; }
    public string? SourcePath { get; set; }
    public string? LastAction { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdatedUtc { get; set; }
}
