using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Integration.Tests;

/// <summary>
/// Shared helper for pulling and extracting the hello world chart.
/// </summary>
internal static class HelloWorldChart
{
    public const string ChartRef = "oci://ghcr.io/andrewiankidd/charts/cross-platform-hello";

    public static async Task<(string chartPath, string valuesPath, string dekomposeConfigPath)> PullAndExtractAsync(ProcessRunner runner)
    {
        var chartsDir = CrossposeEnvironment.HelmChartsDirectory;
        var helm = new HelmClient(runner, NullLogger.Instance);

        var chartPath = await helm.PullAsync(ChartRef, version: null, chartsDir);
        Assert.NotNull(chartPath);

        await helm.ExtractCrossposeFilesAsync(chartPath);

        var baseName = Path.GetFileNameWithoutExtension(chartPath);
        var valuesPath = Path.Combine(chartsDir, $"{baseName}.values.yaml");
        var dekomposeConfigPath = Path.Combine(chartsDir, $"{baseName}.dekompose.yml");

        Assert.True(File.Exists(valuesPath), $"crosspose/values.yaml not found in chart — expected {valuesPath}");
        Assert.True(File.Exists(dekomposeConfigPath), $"crosspose/dekompose.yml not found in chart — expected {dekomposeConfigPath}");

        return (chartPath, valuesPath, dekomposeConfigPath);
    }

    public static string FindProject(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Crosspose.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var project = Path.Combine(dir.FullName, "src", name, $"{name}.csproj");
        Assert.True(File.Exists(project), $"Project not found: {project}");
        return project;
    }
}

/// <summary>
/// Verifies the chart can be dekomposed. Fast, no containers.
/// </summary>
[Trait("Category", "Integration")]
public class DekomposeTests
{
    [Fact]
    public async Task Chart_Dekomposes_Successfully()
    {
        using var loggerFactory = CrossposeLoggerFactory.Create(LogLevel.Information);
        var runner = new ProcessRunner(loggerFactory.CreateLogger<ProcessRunner>());

        var (chartPath, valuesPath, dekomposeConfigPath) = await HelloWorldChart.PullAndExtractAsync(runner);

        var result = await runner.RunAsync("dotnet",
            $"run --project \"{HelloWorldChart.FindProject("Crosspose.Dekompose.Cli")}\" -- " +
            $"--chart \"{chartPath}\" --values \"{valuesPath}\" --dekompose-config \"{dekomposeConfigPath}\" " +
            $"--infra --remap-ports --compress");

        Assert.True(result.IsSuccess, $"Dekompose failed:\n{result.StandardError}\n{result.StandardOutput}");

        var bundle = Directory.GetFiles(CrossposeEnvironment.OutputDirectory, "*.zip")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
        Assert.NotNull(bundle);
    }
}

/// <summary>
/// Full end-to-end: pull → dekompose → deploy → up → doctor fix → health check → teardown.
/// Slow (~5min+). Requires Docker Desktop (Windows mode), WSL/Podman, Helm, and elevation for port proxies.
/// Run with: dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public class FullPipelineTests : IAsyncLifetime
{
    private const int HealthTimeoutSeconds = 300;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ProcessRunner _runner;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private string? _deployDir;

    public FullPipelineTests()
    {
        _loggerFactory = CrossposeLoggerFactory.Create(LogLevel.Information);
        _runner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // TODO: teardown disabled while debugging — containers stay up for inspection
        // if (!string.IsNullOrWhiteSpace(_deployDir) && Directory.Exists(_deployDir))
        // {
        //     try { await RunCliAsync("down", "--dir", _deployDir); } catch { }
        //     try { await RunCliAsync("remove", "--dir", _deployDir); } catch { }
        // }
        // try { await _runner.RunAsync("docker", "container prune -f"); } catch { }
        // try { await _runner.RunAsync("wsl", "--distribution crosspose-data -- podman container prune -f"); } catch { }

        await Task.CompletedTask;
        _http.Dispose();
        _loggerFactory.Dispose();
    }

    [Fact]
    public async Task Both_Containers_Become_Healthy()
    {
        // Step 1: Pull and extract chart
        var (chartPath, valuesPath, dekomposeConfigPath) = await HelloWorldChart.PullAndExtractAsync(_runner);

        // Step 2: Dekompose via CLI
        var dekompose = await _runner.RunAsync("dotnet",
            $"run --project \"{HelloWorldChart.FindProject("Crosspose.Dekompose.Cli")}\" -- " +
            $"--chart \"{chartPath}\" --values \"{valuesPath}\" --dekompose-config \"{dekomposeConfigPath}\" " +
            $"--infra --remap-ports --compress");
        Assert.True(dekompose.IsSuccess, $"Dekompose failed:\n{dekompose.StandardError}\n{dekompose.StandardOutput}");

        var bundle = Directory.GetFiles(CrossposeEnvironment.OutputDirectory, "*.zip")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
        Assert.NotNull(bundle);

        // Step 3: Deploy via CLI
        var deploy = await RunCliAsync("deploy", bundle);
        Assert.True(deploy.IsSuccess, $"Deploy failed:\n{deploy.StandardError}\n{deploy.StandardOutput}");

        var deployMatch = Regex.Match(deploy.StandardOutput, @"Deployed to:\s*(.+)");
        Assert.True(deployMatch.Success, $"Could not find deployment path in output:\n{deploy.StandardOutput}");
        _deployDir = deployMatch.Groups[1].Value.Trim();

        // Step 4: Up via CLI (may partially fail while images pull — that's OK)
        await RunCliAsync("up", "--dir", _deployDir, "-d");

        // Step 5: Doctor fix via CLI (port proxies, auth)
        await _runner.RunAsync("dotnet",
            $"run --project \"{HelloWorldChart.FindProject("Crosspose.Doctor.Cli")}\" -- --fix");

        // Step 6: Wait for both containers to serve HTTP 200
        var linuxHealthy = false;
        var windowsHealthy = false;
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed.TotalSeconds < HealthTimeoutSeconds)
        {
            var (linuxPort, windowsPort) = await DiscoverPortsAsync();

            if (linuxPort > 0 && !linuxHealthy)
                linuxHealthy = await ProbeHealthAsync($"http://localhost:{linuxPort}/");

            if (windowsPort > 0 && !windowsHealthy)
                windowsHealthy = await ProbeHealthAsync($"http://localhost:{windowsPort}/");

            if (linuxHealthy && windowsHealthy)
                break;

            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        Assert.True(linuxHealthy, $"Linux container did not become healthy within {HealthTimeoutSeconds}s");
        Assert.True(windowsHealthy, $"Windows container did not become healthy within {HealthTimeoutSeconds}s");

        // Step 7: Verify artefacts are in expected directories
        Assert.True(
            Directory.GetFiles(CrossposeEnvironment.HelmChartsDirectory, "cross-platform-hello-*.tgz").Length > 0,
            "Chart should be in helm-charts directory");
        Assert.True(
            Directory.GetFiles(CrossposeEnvironment.OutputDirectory, "*.zip").Length > 0,
            "Bundle should be in dekompose-outputs directory");
        Assert.True(
            Directory.Exists(_deployDir),
            "Deployment directory should exist");
    }

    private async Task<ProcessResult> RunCliAsync(params string[] args)
    {
        var quoted = args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a);
        return await _runner.RunAsync("dotnet",
            $"run --project \"{HelloWorldChart.FindProject("Crosspose.Cli")}\" -- {string.Join(" ", quoted)}");
    }

    private async Task<(int linuxPort, int windowsPort)> DiscoverPortsAsync()
    {
        var linuxPort = 0;
        var windowsPort = 0;

        var ps = await RunCliAsync("ps", "-a");
        foreach (var line in ps.StandardOutput.Split('\n'))
        {
            if (line.Contains("hello-linux", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("hello_linux", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"(\d+):8080");
                if (match.Success) linuxPort = int.Parse(match.Groups[1].Value);
            }

            if (line.Contains("hello-windows", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("hello_windows", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"(\d+):80\b");
                if (match.Success) windowsPort = int.Parse(match.Groups[1].Value);
            }
        }

        return (linuxPort, windowsPort);
    }

    private async Task<bool> ProbeHealthAsync(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }
}
