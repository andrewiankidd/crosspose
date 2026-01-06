using System.Text;
using Microsoft.Extensions.Logging;

namespace Dekompose.Services;

/// <summary>
/// Temporary writer that keeps the output folder layout and documents how to transition
/// the PowerShell prototype (crossposeps) into the .NET pipeline.
/// </summary>
public sealed class ComposeStubWriter
{
    private readonly ILogger<ComposeStubWriter> _logger;

    public ComposeStubWriter(ILogger<ComposeStubWriter> logger)
    {
        _logger = logger;
    }

    public async Task WritePlaceholderAsync(string manifestPath, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var manifestCopyPath = Path.Combine(outputDirectory, "rendered.manifest.yaml");
        File.Copy(manifestPath, manifestCopyPath, overwrite: true);
        _logger.LogInformation("Copied manifest to {ManifestCopy}", manifestCopyPath);

        var guidancePath = Path.Combine(outputDirectory, "TODO.compose-generation.md");
        var guidance = new StringBuilder();
        guidance.AppendLine("# Compose generation placeholder");
        guidance.AppendLine();
        guidance.AppendLine("- Input manifest: " + manifestCopyPath);
        guidance.AppendLine("- Prototype reference: C:\\git\\crossposeps (see src\\Main.ps1 and assets\\scripts\\compose.ps1)");
        guidance.AppendLine("- Desired output pattern: dekompose-outputs\\<chart>-<version>\\docker-compose.<workload>.<os>.yml");
        guidance.AppendLine("- Ideal sample output: C:\\git\\crossposeps\\docker-compose-outputs");
        guidance.AppendLine();
        guidance.AppendLine("## Next steps");
        guidance.AppendLine("1. Port workload/OS detection and port assignment from the PowerShell converter into a C# pipeline.");
        guidance.AppendLine("2. Emit shared resources file (networks, volumes) and workload-specific compose files.");
        guidance.AppendLine("3. Translate ConfigMaps/Secrets into bind mounts or TODO placeholders in compose.");
        guidance.AppendLine("4. Keep config outputs gitignored; only source stays under version control.");
        guidance.AppendLine();
        guidance.AppendLine("This file is generated to preserve the expected folder layout while the C# implementation");
        guidance.AppendLine("is being built. Once the generator is ready, replace this with the actual compose output.");

        await File.WriteAllTextAsync(guidancePath, guidance.ToString(), cancellationToken);
        _logger.LogInformation("Stub guidance written to {GuidancePath}", guidancePath);
    }
}
