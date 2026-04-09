using System.Reflection;
using System.IO.Compression;
using Crosspose.Core.Configuration;
using Crosspose.Core.Deployment;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging;
using Crosspose.Dekompose.Core.Services;
using Microsoft.Extensions.Logging;

DekomposeOptions options;
try
{
    options = ParseArgs(args);
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
    PrintUsage();
    return 1;
}
if (LaunchedOutsideShell())
{
    Console.WriteLine("This is a command line tool.");
    Console.WriteLine("You need to open cmd.exe and run it from there, or use the GUI.");
    return 1;
}
if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}
if (options.ShowVersion)
{
    PrintVersion();
    return 0;
}

if (!string.IsNullOrWhiteSpace(options.DekomposeConfigPath))
{
    try
    {
        CrossposeConfigurationStore.ApplyDekomposeConfigForSession(options.DekomposeConfigPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
        return 1;
    }
}

using var loggerFactory = CrossposeLoggerFactory.Create(LogLevel.Information);
var logger = loggerFactory.CreateLogger("crosspose.dekompose");
var runner = new ProcessRunner(loggerFactory.CreateLogger<ProcessRunner>());
var helm = new HelmTemplateRunner(runner, loggerFactory.CreateLogger<HelmTemplateRunner>());
var composeGenerator = new ComposeGenerator(loggerFactory.CreateLogger<ComposeGenerator>());

var baseOutputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
    ? CrossposeEnvironment.OutputDirectory
    : AppDataLocator.GetPreferredDirectory(options.OutputDirectory);
Directory.CreateDirectory(baseOutputDirectory);

string? renderedManifestPath = options.ManifestPath;
string targetOutputDirectory = baseOutputDirectory;
var epochStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var networkName = $"dekompose-{epochStamp}";

ChartInfo? chartInfo = null;
if (options.ChartPath is not null)
{
    chartInfo = TryReadChartInfo(options.ChartPath)
                ?? TryInferOciChartInfo(options.ChartPath);
    if (!string.IsNullOrWhiteSpace(options.ChartVersion))
    {
        if (chartInfo is not null)
        {
            chartInfo = chartInfo with { Version = options.ChartVersion };
        }
        else
        {
            var inferredName = Path.GetFileName(options.ChartPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(inferredName))
            {
                inferredName = options.ChartPath;
            }
            chartInfo = new ChartInfo(inferredName, options.ChartVersion);
        }
    }
    if (chartInfo is not null)
    {
        string? valuesName = null;
        if (!string.IsNullOrWhiteSpace(options.ValuesPath) && File.Exists(options.ValuesPath))
        {
            var stem = Path.GetFileNameWithoutExtension(options.ValuesPath);
            // Strip chart name+version prefix when the file follows the "<chartbase>.<label>.yaml" convention
            var chartPrefix = $"{chartInfo.Name}-{chartInfo.Version}.";
            valuesName = stem.StartsWith(chartPrefix, StringComparison.OrdinalIgnoreCase) && stem.Length > chartPrefix.Length
                ? stem[chartPrefix.Length..]
                : stem;
        }

        // Folder/bundle name: <chart-product-name>, with -1, -2 ... suffix on collision.
        // Chart name, version, values label and creation timestamp are stored in _chart.yaml inside the bundle.
        var productName = DefinitionDeploymentService.SanitizeForCompose(chartInfo.TgzName ?? chartInfo.Name);
        var targetName = productName;
        var collisionSuffix = 1;
        while (Directory.Exists(Path.Combine(baseOutputDirectory, targetName)) ||
               (options.CompressOutput && File.Exists(Path.Combine(baseOutputDirectory, $"{targetName}.zip"))))
        {
            targetName = $"{productName}-{collisionSuffix++}";
        }

        targetOutputDirectory = Path.Combine(baseOutputDirectory, targetName);
        Directory.CreateDirectory(targetOutputDirectory);

        // Write chart metadata so the GUI can display chart name/version without parsing the filename.
        var chartMetaLines = new System.Text.StringBuilder();
        chartMetaLines.AppendLine($"chartName: {chartInfo.TgzName ?? chartInfo.Name}");
        chartMetaLines.AppendLine($"chartVersion: {chartInfo.Version}");
        if (valuesName is not null)
            chartMetaLines.AppendLine($"valuesLabel: {valuesName}");
        chartMetaLines.AppendLine($"createdAt: {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        File.WriteAllText(Path.Combine(targetOutputDirectory, "_chart.yaml"), chartMetaLines.ToString());
    }
}

// If no chart metadata is available, isolate each run into its own timestamped folder
if (string.Equals(targetOutputDirectory, baseOutputDirectory, StringComparison.OrdinalIgnoreCase))
{
    targetOutputDirectory = Path.Combine(baseOutputDirectory, $"run-{DateTime.Now:yyyyMMddHHmmss}");
    Directory.CreateDirectory(targetOutputDirectory);
}

if (renderedManifestPath is null && options.ChartPath is not null)
{
    logger.LogInformation("Templating Helm chart from {ChartPath}", options.ChartPath);
    var helmResult = await helm.RenderAsync(options.ChartPath, options.ValuesPath, targetOutputDirectory, options.ChartVersion, CancellationToken.None);
    if (!helmResult.Succeeded)
    {
        logger.LogError("Helm templating failed; exiting early.");
        return 1;
    }

    renderedManifestPath = helmResult.RenderedManifestPath;

    // Persist the values file that produced this render for traceability.
    try
    {
        var valuesCopy = Path.Combine(targetOutputDirectory, "_values.yaml");
        if (!string.IsNullOrWhiteSpace(options.ValuesPath) && File.Exists(options.ValuesPath))
        {
            File.Copy(options.ValuesPath, valuesCopy, overwrite: true);
        }
        else
        {
            File.WriteAllText(valuesCopy, "# Using chart defaults (no explicit values supplied).\n");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to persist values snapshot.");
    }
}

if (renderedManifestPath is null)
{
    logger.LogError("No manifest supplied. Pass --manifest to use a pre-rendered file or --chart to invoke helm template.");
    PrintUsage();
    return 1;
}

var ruleSets = CrossposeEnvironment.GetDekomposeRules(chartInfo?.Name, chartInfo?.TgzName);
logger.LogInformation("Resolved {RuleCount} Dekompose rule sets for chart {ChartName}.",
    ruleSets.Count,
    chartInfo?.Name ?? "(unknown)");

await composeGenerator.GenerateAsync(
    renderedManifestPath,
    targetOutputDirectory,
    networkName,
    options.IncludeInfraEstimates,
    options.RemapServicePorts,
    ruleSets,
    CancellationToken.None);
logger.LogInformation("Scaffold complete. Review docker-compose files in {OutputDirectory}.", targetOutputDirectory);

if (options.CompressOutput)
{
    var zipName = Path.GetFileName(targetOutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    var zipPath = Path.Combine(baseOutputDirectory, $"{zipName}.zip");
    try
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(targetOutputDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        logger.LogInformation("Compressed output to {ZipPath}", zipPath);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to compress output folder {Folder}", targetOutputDirectory);
        return 1;
    }
    finally
    {
        try
        {
            if (Directory.Exists(targetOutputDirectory))
            {
                Directory.Delete(targetOutputDirectory, recursive: true);
                logger.LogInformation("Removed temporary output folder {Folder} after compression.", targetOutputDirectory);
            }
        }
        catch (Exception cleanupEx)
        {
            logger.LogWarning(cleanupEx, "Failed to remove temporary output folder {Folder}.", targetOutputDirectory);
        }
    }

    return 0;
}

return 0;

static void PrintUsage()
{
    Console.WriteLine("crosspose dekompose");
    Console.WriteLine($"  Version: {GetVersion()}");
    Console.WriteLine("  --chart <path>       Helm chart directory to template (uses `helm template`).");
    Console.WriteLine("  --chart-version <v>  Optional chart version passed to helm.");
    Console.WriteLine("  --values <file>      Optional values.yaml passed to helm.");
    Console.WriteLine("  --dekompose-config <file>  Optional dekompose.yml to merge into crosspose.yml before render.");
    Console.WriteLine("  --manifest <file>    Use an already-rendered manifest instead of running helm.");
    Console.WriteLine("  --output <dir>       Output folder (default: ./dekompose-outputs).");
    Console.WriteLine("  --compress           Also write a zip of the generated output (default: off).");
    Console.WriteLine("  --infra              Scaffold supporting infrastructure (e.g. MSSQL) and update dependent env vars.");
    Console.WriteLine("  --remap-ports        Remap in-cluster service URLs (service.default.svc) to localhost mappings.");
    Console.WriteLine("  --help               Show this help text.");
    Console.WriteLine("  --version, -v        Show version.");
}

static DekomposeOptions ParseArgs(string[] args)
{
    var result = new DekomposeOptions();
    var queue = new Queue<string>(args);

    while (queue.Count > 0)
    {
        var token = queue.Dequeue();
        switch (token)
        {
            case "--chart":
                result.ChartPath = RequireValue(queue, token);
                break;
            case "--chart-version":
                result.ChartVersion = RequireValue(queue, token);
                break;
            case "--values":
                result.ValuesPath = RequireValue(queue, token);
                break;
            case "--dekompose-config":
                result.DekomposeConfigPath = RequireValue(queue, token);
                break;
            case "--manifest":
            case "--rendered-manifest":
                result.ManifestPath = RequireValue(queue, token);
                break;
            case "--output":
                result.OutputDirectory = RequireValue(queue, token);
                break;
            case "--compress":
                result.CompressOutput = true;
                break;
            case "--infra":
            case "--estimate-infra":
                result.IncludeInfraEstimates = true;
                break;
            case "--remap-ports":
                result.RemapServicePorts = true;
                break;
            case "--help":
            case "-h":
            case "/?":
                result.ShowHelp = true;
                break;
            case "--version":
            case "-v":
                result.ShowVersion = true;
                break;
            default:
                Console.WriteLine($"Unknown argument: {token}");
                result.ShowHelp = true;
                break;
        }
    }

    return result;

    static string RequireValue(Queue<string> queue, string optionName)
    {
        if (queue.Count == 0)
        {
            throw new ArgumentException($"Missing value for {optionName}");
        }

        return queue.Dequeue();
    }
}

static bool LaunchedOutsideShell() => !CrossposeEnvironment.IsShellAvailable;

static ChartInfo? TryReadChartInfo(string chartPath)
{
    try
    {
        // Directory with Chart.yaml
        var chartYaml = Path.Combine(chartPath, "Chart.yaml");
        if (File.Exists(chartYaml))
        {
            string? name = null;
            string? version = null;
            foreach (var line in File.ReadLines(chartYaml))
            {
                if (name is null && line.TrimStart().StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    name = line.Split(':', 2)[1].Trim();
                if (version is null && line.TrimStart().StartsWith("version:", StringComparison.OrdinalIgnoreCase))
                    version = line.Split(':', 2)[1].Trim();
                if (name is not null && version is not null) break;
            }
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
                return new ChartInfo(name, version);
        }

        // .tgz file — read Chart.yaml from inside the archive first
        if (chartPath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var fs = File.OpenRead(chartPath);
                using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
                using var tar = new System.Formats.Tar.TarReader(gz);
                System.Formats.Tar.TarEntry? entry;
                while ((entry = tar.GetNextEntry()) != null)
                {
                    var entryName = entry.Name.Replace('\\', '/').TrimStart('/');
                    // Match only the root Chart.yaml: exactly one directory level (<chartdir>/Chart.yaml)
                    var slashCount = entryName.Count(c => c == '/');
                    if (slashCount != 1 || !entryName.EndsWith("/Chart.yaml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    using var reader = new StreamReader(entry.DataStream!);
                    string? name = null;
                    string? version = null;
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (name is null && line.TrimStart().StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                            name = line.Split(':', 2)[1].Trim();
                        if (version is null && line.TrimStart().StartsWith("version:", StringComparison.OrdinalIgnoreCase))
                            version = line.Split(':', 2)[1].Trim();
                        if (name is not null && version is not null) break;
                    }
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
                    {
                        // Record the tgz filename-derived name as an alias for rule matching.
                        // Dekompose.yml authors typically use the tgz product name (e.g. "helm-platform")
                        // which may differ from the internal Chart.yaml name (e.g. "platform").
                        string? tgzDerivedName = null;
                        var tgzBase = Path.GetFileNameWithoutExtension(chartPath);
                        var tgzHyphen = tgzBase.LastIndexOf('-');
                        if (tgzHyphen > 0 && tgzHyphen < tgzBase.Length - 1 && char.IsDigit(tgzBase[tgzHyphen + 1]))
                            tgzDerivedName = tgzBase[..tgzHyphen];
                        return new ChartInfo(name, version, tgzDerivedName);
                    }
                    break; // only one Chart.yaml expected
                }
            }
            catch { /* fall through to filename parsing */ }

            // Fallback: parse by filename convention: <name>-<version>.tgz
            var baseName = Path.GetFileNameWithoutExtension(chartPath);
            // Version starts at the last hyphen followed by a digit
            var lastHyphen = baseName.LastIndexOf('-');
            if (lastHyphen > 0 && lastHyphen < baseName.Length - 1 && char.IsDigit(baseName[lastHyphen + 1]))
            {
                var name = baseName[..lastHyphen];
                var version = baseName[(lastHyphen + 1)..];
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
                    return new ChartInfo(name, version);
            }
        }

        return null;
    }
    catch
    {
        return null;
    }
}

static void PrintVersion() => Console.WriteLine(GetVersion());
static string GetVersion() => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

static ChartInfo? TryInferOciChartInfo(string chartPath)
{
    // Expect formats like:
    //  oci://registry.azurecr.io/helm/platform
    //  oci://registry.azurecr.io/helm/platform:9.2.415
    //  registry.azurecr.io/helm/platform[:tag]
    var trimmed = chartPath.Replace("oci://", "", StringComparison.OrdinalIgnoreCase);
    var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length < 2) return null; // need host + repo

    // Extract tag if specified
    var repoWithTag = string.Join('/', parts.Skip(1)); // everything after host
    var tagSplit = repoWithTag.Split(':', 2, StringSplitOptions.TrimEntries);
    var repoPath = tagSplit[0];
    var tag = tagSplit.Length == 2 && !string.IsNullOrWhiteSpace(tagSplit[1]) ? tagSplit[1] : "latest";

    if (string.IsNullOrWhiteSpace(repoPath)) return null;
    // Normalise "helm/platform" → "helm-platform" for precise rule matching.
    var name = repoPath.Replace('/', '-').Trim('-');
    if (string.IsNullOrWhiteSpace(name)) return null;

    return new ChartInfo(name, tag);
}

internal sealed record DekomposeOptions
{
    public string? ChartPath { get; set; }
    public string? ChartVersion { get; set; }
    public string? ValuesPath { get; set; }
    public string? DekomposeConfigPath { get; set; }
    public string? ManifestPath { get; set; }
    public string? OutputDirectory { get; set; }
    public bool CompressOutput { get; set; }
    public bool IncludeInfraEstimates { get; set; }
    public bool RemapServicePorts { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
}

internal sealed record ChartInfo(string Name, string Version, string? TgzName = null);
