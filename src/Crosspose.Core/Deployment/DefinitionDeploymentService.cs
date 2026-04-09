using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crosspose.Core.Configuration;

namespace Crosspose.Core.Deployment;

public sealed class DefinitionDeploymentRequest
{
    public string SourcePath { get; init; } = string.Empty;
    public string BaseDirectory { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string? ChartVersion { get; init; }
}

public sealed class DefinitionDeploymentResult
{
    public string TargetPath { get; init; } = string.Empty;
    public DeploymentMetadata Metadata { get; init; } = new();
}

public sealed class DefinitionDeploymentService
{
    public Task<DefinitionDeploymentResult> PrepareAsync(
        DefinitionDeploymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ArgumentException("Source path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.BaseDirectory)) throw new ArgumentException("Base directory is required.", nameof(request));

        var deploymentRoot = Path.GetFullPath(request.BaseDirectory);
        Directory.CreateDirectory(deploymentRoot);

        // Sanitize to compose-compatible name: lowercase, dots/spaces → hyphens, strip invalid chars.
        var projectSegment = SanitizeForCompose(request.ProjectName);

        // Collision handling: append -1, -2, ... if a deployment with this name already exists.
        var targetName = projectSegment;
        var suffix = 1;
        while (Directory.Exists(Path.Combine(deploymentRoot, targetName)))
        {
            targetName = $"{projectSegment}-{suffix++}";
        }

        var targetDirectory = Path.Combine(deploymentRoot, targetName);
        Directory.CreateDirectory(targetDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(request.SourcePath))
        {
            CopyDirectoryContents(request.SourcePath, targetDirectory);
        }
        else if (File.Exists(request.SourcePath) && Path.GetExtension(request.SourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(request.SourcePath, targetDirectory, overwriteFiles: true);
        }
        else
        {
            throw new FileNotFoundException($"Definition source not found: {request.SourcePath}", request.SourcePath);
        }

        var metadata = new DeploymentMetadata
        {
            Project = projectSegment,
            ChartVersion = request.ChartVersion,
            SourcePath = request.SourcePath,
            LastAction = "Prepared"
        };

        DeploymentMetadataStore.Write(targetDirectory, metadata);
        var portProxyRequirements = PortProxyRequirementLoader.Load(targetDirectory);
        RegisterPortProxyRequirements(portProxyRequirements);

        return Task.FromResult(new DefinitionDeploymentResult
        {
            TargetPath = targetDirectory,
            Metadata = metadata
        });
    }

    private static void RegisterPortProxyRequirements(IReadOnlyCollection<PortProxyRequirement> requirements)
    {
        if (requirements is null || requirements.Count == 0)
        {
            return;
        }

        var filtered = requirements
            .Where(entry => entry.Port > 0)
            .ToList();

        if (filtered.Count == 0)
        {
            return;
        }

        var keys = filtered
            .Select(entry => PortProxyKey.Format(entry.Port, entry.ConnectPort, entry.Network))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DoctorCheckRegistrar.EnsureChecks(keys);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            var destinationFolder = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }
            File.Copy(file, destination, overwrite: true);
        }
    }

    /// <summary>
    /// Normalises a string to a compose-compatible segment: lowercase, dots/spaces become hyphens,
    /// other non-alphanumeric (except hyphens and underscores) are removed.
    /// </summary>
    public static string SanitizeForCompose(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "crosspose";
        var chars = name.ToLowerInvariant().Select(c => c == '.' || c == ' ' ? '-' : c);
        var sanitized = new string(chars.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray()).Trim('-', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "crosspose" : sanitized;
    }
}
