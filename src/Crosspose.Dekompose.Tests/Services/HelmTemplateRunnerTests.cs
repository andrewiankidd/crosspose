using Crosspose.Core.Diagnostics;
using Crosspose.Dekompose.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Dekompose.Tests.Services;

public class HelmTemplateRunnerTests
{
    [Fact]
    public void HelmRenderResult_Success_Properties()
    {
        var result = new HelmRenderResult(true, "/tmp/rendered.yaml", true, null);
        Assert.True(result.Succeeded);
        Assert.True(result.UsedHelm);
        Assert.Null(result.Error);
    }

    [Fact]
    public void HelmRenderResult_Failure_Properties()
    {
        var result = new HelmRenderResult(false, "/tmp/rendered.yaml", false, "chart not found");
        Assert.False(result.Succeeded);
        Assert.Equal("chart not found", result.Error);
    }

    [Fact]
    public async Task RenderAsync_WithRealHelm_RequiresHelmInstalled()
    {
        // Integration test — only runs if helm is on PATH
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var probe = await runner.RunAsync("helm", "version --short");
        if (!probe.IsSuccess)
        {
            return; // helm not installed, skip
        }

        // Helm is available but we don't have a real chart — verify it fails gracefully
        var logger = NullLogger<HelmTemplateRunner>.Instance;
        var helm = new HelmTemplateRunner(runner, logger);
        var tempDir = Path.Combine(Path.GetTempPath(), "crosspose-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = await helm.RenderAsync(tempDir, null, tempDir, null, CancellationToken.None);
            // Should fail because there's no Chart.yaml in the temp dir
            Assert.False(result.Succeeded);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
