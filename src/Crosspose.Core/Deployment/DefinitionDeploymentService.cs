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
    public string? Version { get; init; }
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

        var projectSegment = SanitizeSegment(request.ProjectName);
        var versionSegment = SanitizeSegment(request.Version);
        if (string.IsNullOrWhiteSpace(versionSegment) || versionSegment.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            versionSegment = DateTime.Now.ToString("yyyyMMddHHmmss");
        }

        var targetDirectory = Path.Combine(deploymentRoot, projectSegment, versionSegment);
        if (Directory.Exists(targetDirectory))
        {
            Directory.Delete(targetDirectory, recursive: true);
        }

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
            Project = request.ProjectName,
            Version = request.Version,
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
            .Select(entry => PortProxyKey.Format(entry.Port, entry.Network))
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

    private static string SanitizeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "default";
        var trimmed = segment.Trim();
        var chars = new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            chars[i] = Path.GetInvalidPathChars().Contains(ch) ? '_' : ch;
        }
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }
}
