using Crosspose.Dekompose.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Dekompose.Tests.Services;

public class ComposeGeneratorTests : IDisposable
{
    private readonly ComposeGenerator _generator;
    private readonly string _tempDir;

    public ComposeGeneratorTests()
    {
        _generator = new ComposeGenerator(NullLogger<ComposeGenerator>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), "crosspose-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteManifest(string yaml)
    {
        var path = Path.Combine(_tempDir, "manifest.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    // --- Deployment with Windows nodeSelector ---
    private const string WindowsDeploymentManifest = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: web-api
        spec:
          selector:
            matchLabels:
              app: web-api
          template:
            metadata:
              labels:
                app: web-api
            spec:
              nodeSelector:
                kubernetes.io/os: windows
              containers:
              - name: web-api
                image: myregistry.azurecr.io/web-api:latest
                ports:
                - containerPort: 80
                env:
                - name: ASPNETCORE_URLS
                  value: "http://+:80"
        """;

    // --- Deployment with Linux nodeSelector ---
    private const string LinuxDeploymentManifest = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: worker
        spec:
          selector:
            matchLabels:
              app: worker
          template:
            metadata:
              labels:
                app: worker
            spec:
              nodeSelector:
                kubernetes.io/os: linux
              containers:
              - name: worker
                image: myregistry.azurecr.io/worker:latest
                ports:
                - containerPort: 8080
        """;

    // --- Multi-document manifest with both OS types ---
    private const string MixedManifest = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: frontend
        spec:
          selector:
            matchLabels:
              app: frontend
          template:
            metadata:
              labels:
                app: frontend
            spec:
              nodeSelector:
                kubernetes.io/os: windows
              containers:
              - name: frontend
                image: frontend:latest
                ports:
                - containerPort: 443
        ---
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: backend
        spec:
          selector:
            matchLabels:
              app: backend
          template:
            metadata:
              labels:
                app: backend
            spec:
              nodeSelector:
                kubernetes.io/os: linux
              containers:
              - name: backend
                image: backend:latest
                ports:
                - containerPort: 5000
        """;

    [Fact]
    public async Task GenerateAsync_WindowsDeployment_CreatesWindowsComposeFile()
    {
        var manifest = WriteManifest(WindowsDeploymentManifest);
        var outDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outDir);

        await _generator.GenerateAsync(manifest, outDir, "test-net", false, false, Array.Empty<Core.Configuration.DekomposeRuleSet>(), CancellationToken.None);

        var files = Directory.GetFiles(outDir, "docker-compose.*.yml");
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.Contains("windows", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_LinuxDeployment_CreatesLinuxComposeFile()
    {
        var manifest = WriteManifest(LinuxDeploymentManifest);
        var outDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outDir);

        await _generator.GenerateAsync(manifest, outDir, "test-net", false, false, Array.Empty<Core.Configuration.DekomposeRuleSet>(), CancellationToken.None);

        var files = Directory.GetFiles(outDir, "docker-compose.*.yml");
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.Contains("linux", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_MixedManifest_CreatesBothComposeFiles()
    {
        var manifest = WriteManifest(MixedManifest);
        var outDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outDir);

        await _generator.GenerateAsync(manifest, outDir, "test-net", false, false, Array.Empty<Core.Configuration.DekomposeRuleSet>(), CancellationToken.None);

        var files = Directory.GetFiles(outDir, "docker-compose.*.yml");
        Assert.True(files.Length >= 2, $"Expected at least 2 compose files, got {files.Length}");
    }

    [Fact]
    public async Task GenerateAsync_EmptyManifest_ProducesNoFiles()
    {
        var manifest = WriteManifest("");
        var outDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outDir);

        await _generator.GenerateAsync(manifest, outDir, "test-net", false, false, Array.Empty<Core.Configuration.DekomposeRuleSet>(), CancellationToken.None);

        var composeFiles = Directory.GetFiles(outDir, "docker-compose.*.yml");
        Assert.Empty(composeFiles);
    }

    [Fact]
    public async Task GenerateAsync_ManifestWithConfigMap_HandlesGracefully()
    {
        var manifest = WriteManifest("""
            apiVersion: v1
            kind: ConfigMap
            metadata:
              name: app-config
            data:
              setting: value
            """);
        var outDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outDir);

        // Should not throw — ConfigMaps alone don't produce compose files
        await _generator.GenerateAsync(manifest, outDir, "test-net", false, false, Array.Empty<Core.Configuration.DekomposeRuleSet>(), CancellationToken.None);
    }

    [Fact]
    public async Task GenerateAsync_OutputFilesFollowNamingConvention()
    {
        var manifest = WriteManifest(WindowsDeploymentManifest);
        var outDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outDir);

        await _generator.GenerateAsync(manifest, outDir, "test-net", false, false, Array.Empty<Core.Configuration.DekomposeRuleSet>(), CancellationToken.None);

        var files = Directory.GetFiles(outDir, "docker-compose.*.yml");
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            // Expected: docker-compose.<workload>.<os>.yml
            var parts = name.Split('.');
            Assert.True(parts.Length >= 4, $"File '{name}' doesn't match docker-compose.<workload>.<os>.yml pattern");
            Assert.Equal("docker-compose", parts[0]);
            Assert.Equal("yml", parts[^1]);
        }
    }
}
