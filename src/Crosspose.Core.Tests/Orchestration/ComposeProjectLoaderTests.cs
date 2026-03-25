using Crosspose.Core.Orchestration;

namespace Crosspose.Core.Tests.Orchestration;

public class ComposeProjectLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ComposeProjectLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "crosspose-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_FindsWindowsAndLinuxComposeFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.web.windows.yml"), "version: '3'");
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.web.linux.yml"), "version: '3'");

        using var layout = ComposeProjectLoader.Load(_tempDir);

        Assert.Single(layout.WindowsFiles);
        Assert.Single(layout.LinuxFiles);
        Assert.Equal("web", layout.WindowsFiles[0].Workload);
        Assert.Equal("windows", layout.WindowsFiles[0].Os);
        Assert.Equal(ComposePlatform.Docker, layout.WindowsFiles[0].Platform);
        Assert.Equal("linux", layout.LinuxFiles[0].Os);
        Assert.Equal(ComposePlatform.Podman, layout.LinuxFiles[0].Platform);
    }

    [Fact]
    public void Load_MultipleWorkloads_AllDiscovered()
    {
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.api.windows.yml"), "");
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.api.linux.yml"), "");
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.worker.linux.yml"), "");

        using var layout = ComposeProjectLoader.Load(_tempDir);

        Assert.Single(layout.WindowsFiles);
        Assert.Equal(2, layout.LinuxFiles.Count);
    }

    [Fact]
    public void Load_WorkloadFilter_FiltersCorrectly()
    {
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.api.windows.yml"), "");
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.worker.linux.yml"), "");

        using var layout = ComposeProjectLoader.Load(_tempDir, workloadFilter: "api");

        Assert.Single(layout.WindowsFiles);
        Assert.Empty(layout.LinuxFiles);
    }

    [Fact]
    public void Load_WorkloadFilter_NoMatch_Throws()
    {
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.api.windows.yml"), "");

        Assert.Throws<InvalidOperationException>(() =>
            ComposeProjectLoader.Load(_tempDir, workloadFilter: "nonexistent"));
    }

    [Fact]
    public void Load_NoComposeFiles_Throws()
    {
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "nothing here");

        Assert.Throws<DirectoryNotFoundException>(() =>
            ComposeProjectLoader.Load(_tempDir));
    }

    [Fact]
    public void Load_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ComposeProjectLoader.Load(""));
    }

    [Fact]
    public void Load_NonexistentDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            ComposeProjectLoader.Load(Path.Combine(_tempDir, "does-not-exist")));
    }

    [Fact]
    public void Load_ProjectNameDerivedFromDirectoryName()
    {
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.svc.linux.yml"), "");

        using var layout = ComposeProjectLoader.Load(_tempDir);

        Assert.Equal(Path.GetFileName(_tempDir), layout.ProjectName);
    }

    [Fact]
    public void Load_IgnoresFilesWithTooFewParts()
    {
        // docker-compose.yml has only 2 parts after split — should be ignored
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.yml"), "");
        File.WriteAllText(Path.Combine(_tempDir, "docker-compose.web.linux.yml"), "");

        using var layout = ComposeProjectLoader.Load(_tempDir);

        Assert.Single(layout.LinuxFiles);
        Assert.Empty(layout.WindowsFiles);
    }

    [Fact]
    public void Load_SubdirectoryFallback_FindsFilesInNestedDir()
    {
        var subDir = Path.Combine(_tempDir, "my-output");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "docker-compose.app.windows.yml"), "");

        using var layout = ComposeProjectLoader.Load(_tempDir);

        Assert.Single(layout.WindowsFiles);
    }
}
