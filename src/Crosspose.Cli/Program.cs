using Crosspose.Core.Configuration;
using Crosspose.Core.Deployment;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging;
using Crosspose.Core.Orchestration;
using Crosspose.Core.Sources;
using Microsoft.Extensions.Logging;
using Crosspose.Doctor.Checks;
using System.Reflection;

if (LaunchedOutsideShell())
{
    Console.WriteLine("This is a command line tool.");
    Console.WriteLine("You need to open cmd.exe and run it from there, or use the GUI.");
    return 1;
}

using var loggerFactory = CrossposeLoggerFactory.Create(LogLevel.Information);
var logger = loggerFactory.CreateLogger("crosspose");
var processRunner = new ProcessRunner(loggerFactory.CreateLogger<ProcessRunner>());
var docker = new DockerContainerRunner(processRunner);
var podman = new PodmanContainerRunner(processRunner, runInsideWsl: true, wslDistribution: CrossposeEnvironment.WslDistro);
var combined = new CombinedContainerPlatformRunner(docker, podman);
var helmClient = new HelmClient(processRunner, loggerFactory.CreateLogger<HelmClient>());
var ociStore = new OciRegistryStore(loggerFactory.CreateLogger<OciRegistryStore>());
var composeOrchestrator = new ComposeOrchestrator(docker, podman, loggerFactory);

if (args.Length == 0 || args[0] is "--help" or "-h" or "/?")
{
    PrintUsage();
    return 0;
}
if (args[0] is "--version" or "-v")
{
    PrintVersion();
    return 0;
}

var command = args[0].ToLowerInvariant();
var remaining = args.Skip(1).ToArray();

switch (command)
{
    case "ps":
        var includeAll = remaining.Any(a => a is "-a" or "--all");
        await ListContainersAsync(includeAll);
        return 0;
    case "compose":
        {
            var composeOptions = ParseComposeArgs(remaining);
            return await RunComposeAsync(composer: composeOptions);
        }
case "up":
case "down":
case "restart":
case "stop":
case "start":
case "logs":
case "top":
        {
            var prefixed = new[] { command }.Concat(remaining).ToArray();
            var composeOptions = ParseComposeArgs(prefixed);
            return await RunComposeAsync(composeOptions);
        }
    case "remove":
        return await RemoveDeployment(remaining);
    case "deploy":
        return await DeployAsync(remaining);
    case "container":
        return await HandleContainerAsync(remaining);
    case "images":
        return await HandleImagesAsync(remaining);
    case "volumes":
        return await HandleVolumesAsync(remaining);
    case "bundles":
        return await HandleBundlesAsync(remaining);
    case "deployments":
        return await HandleDeploymentsAsync(remaining);
    case "charts":
        return await HandleChartsAsync(remaining);
    case "sources":
        return await HandleSourcesAsync(remaining);
    default:
        logger.LogError("Unknown command '{Command}'. Run with --help to see all commands.", command);
        PrintUsage();
        return 1;
}

async Task ListContainersAsync(bool includeStopped)
{
    var detailTask = combined.GetContainersDetailedAsync(includeAll: includeStopped);
    var rawTask = combined.GetContainersAsync(includeAll: includeStopped);
    await Task.WhenAll(detailTask, rawTask);

    var raw = rawTask.Result;
    if (raw.HasError && !string.IsNullOrWhiteSpace(raw.Error))
    {
        Console.WriteLine(raw.Error);
    }

    var data = detailTask.Result;
    Console.WriteLine($"{"OS",-3} {"PLATFORM",-8} {"CONTAINER",-20} {"IMAGE",-24} STATUS");
    foreach (var c in data)
    {
        Console.WriteLine($"{c.HostPlatform,-3} {c.Platform,-8} {Trunc(c.Name,20),-20} {Trunc(c.Image,24),-24} {c.Status}");
    }

}

static string Trunc(string value, int max)
{
    if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
    return value[..(max - 1)] + "…";
}

static void PrintUsage()
{
    Console.WriteLine("crosspose usage:");
    Console.WriteLine($"  Version: {GetVersion()}");
    Console.WriteLine();
    Console.WriteLine("Container listing:");
    Console.WriteLine("  ps [--all|-a]                        List docker + podman containers.");
    Console.WriteLine();
    Console.WriteLine("Individual container operations:");
    Console.WriteLine("  container rm <name>                  Force-remove a container.");
    Console.WriteLine("  container stop <name>                Stop a running container.");
    Console.WriteLine("  container start <name>               Start a stopped container.");
    Console.WriteLine("  container logs <name> [--tail N]     Print container logs.");
    Console.WriteLine("  container inspect <name>             Print container inspect JSON.");
    Console.WriteLine();
    Console.WriteLine("Images:");
    Console.WriteLine("  images [ls]                          List images across docker + podman.");
    Console.WriteLine("  images rm <id>                       Force-remove an image.");
    Console.WriteLine("  images prune                         Remove all unused images.");
    Console.WriteLine();
    Console.WriteLine("Volumes:");
    Console.WriteLine("  volumes [ls]                         List volumes across docker + podman.");
    Console.WriteLine("  volumes rm <name>                    Remove a volume.");
    Console.WriteLine("  volumes prune                        Remove all unused volumes.");
    Console.WriteLine();
    Console.WriteLine("Compose orchestration (workload level):");
    Console.WriteLine("  up|down|restart|stop|start|logs|top [--dir <path>] [--workload <name>] [-d]");
    Console.WriteLine("  compose <action> [--dir <path>] [--workload <name>] [-d] [--project <name>]");
    Console.WriteLine();
    Console.WriteLine("Bundles (dekomposed compose zips):");
    Console.WriteLine("  bundles [list]                       List available bundles.");
    Console.WriteLine("  bundles rm <name>                    Remove a bundle zip.");
    Console.WriteLine();
    Console.WriteLine("Deployments:");
    Console.WriteLine("  deployments [list]                   List deployed projects.");
    Console.WriteLine("  deploy <bundle> [--project <name>]   Deploy a bundle to the deployments directory.");
    Console.WriteLine("  remove --dir <path>                  Delete a deployment directory.");
    Console.WriteLine();
    Console.WriteLine("Helm charts:");
    Console.WriteLine("  charts [list]                        List downloaded Helm charts.");
    Console.WriteLine();
    Console.WriteLine("Chart sources:");
    Console.WriteLine("  sources list                         List configured Helm & OCI sources.");
    Console.WriteLine("  sources add <url> [--user u --pass p]");
    Console.WriteLine("  sources charts <sourceName>          List charts from a source.");
    Console.WriteLine();
    Console.WriteLine("  --help                               Show this help text.");
    Console.WriteLine("  --version, -v                        Show version.");
}

static ComposeOptions ParseComposeArgs(string[] cliArgs)
{
    var options = new ComposeOptions();
    var queue = new Queue<string>(cliArgs);
    var actionSpecified = false;
    while (queue.Count > 0)
    {
        var token = queue.Dequeue();
        switch (token)
        {
            case "--action":
                options.Action = ParseComposeAction(RequireValue(queue, token));
                actionSpecified = true;
                break;
            case "--workload":
                options.Workload = RequireValue(queue, token);
                break;
            case "--dir":
            case "--directory":
                options.Directory = RequireValue(queue, token);
                break;
            case "--project":
                options.ProjectName = RequireValue(queue, token);
                break;
            case "--detached":
            case "-d":
                options.Detached = true;
                break;
            case "--":
                while (queue.Count > 0) options.ExtraArgs.Add(queue.Dequeue());
                break;
            default:
                if (!actionSpecified && ComposeActionExtensions.TryParse(token, out var parsed))
                {
                    options.Action = parsed;
                    actionSpecified = true;
                }
                else
                {
                    options.ExtraArgs.Add(token);
                }
                break;
        }
    }

    return options;
}

static ComposeAction ParseComposeAction(string token)
{
    if (ComposeActionExtensions.TryParse(token, out var action)) return action;
    throw new ArgumentException($"Unknown compose action '{token}'.");
}

static string RequireValue(Queue<string> queue, string option)
{
    if (queue.Count == 0)
    {
        throw new ArgumentException($"Missing value for {option}");
    }
    return queue.Dequeue();
}

async Task<int> RunComposeAsync(ComposeOptions composer)
{
    try
    {
        var request = new ComposeExecutionRequest(
            SourcePath: string.IsNullOrWhiteSpace(composer.Directory) ? CrossposeEnvironment.OutputDirectory : composer.Directory,
            Action: composer.Action,
            Workload: composer.Workload,
            Detached: composer.Detached,
            ProjectName: composer.ProjectName,
            AdditionalArguments: composer.ExtraArgs);

        var result = await composeOrchestrator.ExecuteAsync(request);
        var hasError = false;
        if (result.DockerResult is not null)
        {
            Console.WriteLine("=== docker compose ===");
            if (!string.IsNullOrWhiteSpace(result.DockerResult.Result.StandardOutput))
            {
                Console.WriteLine(result.DockerResult.Result.StandardOutput.TrimEnd());
            }
            if (!result.DockerResult.Result.IsSuccess && !string.IsNullOrWhiteSpace(result.DockerResult.Result.StandardError))
            {
                hasError = true;
                Console.Error.WriteLine(result.DockerResult.Result.StandardError.TrimEnd());
            }
        }

        if (result.PodmanResult is not null)
        {
            Console.WriteLine("=== podman compose ===");
            if (!string.IsNullOrWhiteSpace(result.PodmanResult.Result.StandardOutput))
            {
                Console.WriteLine(result.PodmanResult.Result.StandardOutput.TrimEnd());
            }
            if (!result.PodmanResult.Result.IsSuccess && !string.IsNullOrWhiteSpace(result.PodmanResult.Result.StandardError))
            {
                hasError = true;
                Console.Error.WriteLine(result.PodmanResult.Result.StandardError.TrimEnd());
            }
        }

        if (!result.HasAnyOutput)
        {
            Console.WriteLine("No compose files were found for the requested workload.");
            return 1;
        }

        return hasError ? 1 : 0;
    }
    catch (Exception ex) when (
        composer.Action != ComposeAction.Up &&
        (ex is DirectoryNotFoundException || ex is InvalidOperationException) &&
        ex.Message.Contains("compose", StringComparison.OrdinalIgnoreCase))
    {
        // No compose files in the target directory — deployment is already absent, nothing to bring down.
        logger.LogWarning("No compose files found for {Action} — treating as already done.", composer.Action);
        Console.WriteLine($"Nothing to {composer.Action.ToCommand()} (no compose files found).");
        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Compose execution failed.");
        Console.WriteLine($"Compose execution failed: {ex.Message}");
        return 1;
    }
}


static bool LaunchedOutsideShell() => !CrossposeEnvironment.IsShellAvailable;

static void PrintVersion() => Console.WriteLine(GetVersion());
static string GetVersion() => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

// ── Container ────────────────────────────────────────────────────────────────

async Task<(ContainerProcessInfo? info, string qualifiedId)> ResolveContainerAsync(string nameOrId)
{
    var all = await combined.GetContainersDetailedAsync(includeAll: true);
    var match = all.FirstOrDefault(c =>
        c.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
        c.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
        c.Id.StartsWith(nameOrId, StringComparison.OrdinalIgnoreCase));
    if (match is null) return (null, string.Empty);
    return (match, $"{match.Platform}:{match.Id}");
}

async Task<int> HandleContainerAsync(string[] opts)
{
    if (opts.Length == 0)
    {
        Console.WriteLine("container requires a subcommand: start, stop, rm, logs, inspect");
        return 1;
    }
    var sub = opts[0].ToLowerInvariant();
    var rest = opts.Skip(1).ToArray();
    var name = rest.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(name))
    {
        Console.WriteLine($"container {sub} requires a container name or id.");
        return 1;
    }

    var (info, qualifiedId) = await ResolveContainerAsync(name);
    if (info is null)
    {
        Console.WriteLine($"Container not found: {name}");
        return 1;
    }

    switch (sub)
    {
        case "start":
            return await combined.StartContainerAsync(qualifiedId) ? 0 : 1;
        case "stop":
            return await combined.StopContainerAsync(qualifiedId) ? 0 : 1;
        case "rm":
        case "remove":
            var ok = await combined.RemoveContainerAsync(qualifiedId);
            if (ok) Console.WriteLine($"Removed: {info.Name}");
            else Console.WriteLine($"Failed to remove: {info.Name}");
            return ok ? 0 : 1;
        case "logs":
        {
            var tail = 500;
            var tailArg = rest.SkipWhile(a => a != "--tail").Skip(1).FirstOrDefault();
            if (tailArg is not null) int.TryParse(tailArg, out tail);
            var result = await combined.GetContainerLogsAsync(qualifiedId, tail);
            if (!string.IsNullOrWhiteSpace(result.StandardOutput)) Console.WriteLine(result.StandardOutput.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.StandardError)) Console.Error.WriteLine(result.StandardError.TrimEnd());
            return result.IsSuccess ? 0 : 1;
        }
        case "inspect":
        {
            var result = await combined.InspectContainerAsync(qualifiedId);
            Console.WriteLine(result is null ? "{}" : System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        default:
            Console.WriteLine($"Unknown container subcommand '{sub}'. Use: start, stop, rm, logs, inspect");
            return 1;
    }
}

// ── Images ───────────────────────────────────────────────────────────────────

async Task<int> HandleImagesAsync(string[] opts)
{
    var sub = opts.Length > 0 ? opts[0].ToLowerInvariant() : "ls";
    switch (sub)
    {
        case "ls":
        case "list":
        {
            var images = await combined.GetImagesDetailedAsync();
            Console.WriteLine($"{"PLATFORM",-10} {"REPOSITORY",-40} {"TAG",-20} {"ID",-14} SIZE");
            foreach (var img in images)
            {
                var repo = img.Name ?? "<none>";
                var tag = img.Tag ?? "<none>";
                var id = (img.Id ?? string.Empty).Replace("sha256:", "")[..Math.Min(12, (img.Id ?? "").Replace("sha256:", "").Length)];
                Console.WriteLine($"{img.HostPlatform,-10} {Trunc(repo, 40),-40} {Trunc(tag, 20),-20} {id,-14} {img.Size}");
            }
            return 0;
        }
        case "rm":
        case "remove":
        {
            var id = opts.Length > 1 ? opts[1] : null;
            if (string.IsNullOrWhiteSpace(id)) { Console.WriteLine("images rm <id>"); return 1; }
            var images = await combined.GetImagesDetailedAsync();
            var match = images.FirstOrDefault(i =>
                (i.Id ?? string.Empty).StartsWith(id, StringComparison.OrdinalIgnoreCase) ||
                (i.Id ?? string.Empty).Replace("sha256:", "").StartsWith(id, StringComparison.OrdinalIgnoreCase) ||
                $"{i.Name}:{i.Tag}".Equals(id, StringComparison.OrdinalIgnoreCase));
            if (match is null) { Console.WriteLine($"Image not found: {id}"); return 1; }
            var qualifiedId = $"{match.Platform}:{match.Id}";
            var ok = await combined.RemoveImageAsync(qualifiedId);
            Console.WriteLine(ok ? $"Removed: {match.Name}:{match.Tag}" : $"Failed to remove: {id}");
            return ok ? 0 : 1;
        }
        case "prune":
        {
            Console.Write("Remove all unused images from docker and podman? (y/N): ");
            if (!string.Equals(Console.ReadLine(), "y", StringComparison.OrdinalIgnoreCase)) return 0;
            var ok = await combined.PruneImagesAsync();
            Console.WriteLine(ok ? "Done." : "Prune failed (partial).");
            return ok ? 0 : 1;
        }
        default:
            Console.WriteLine("images: ls | rm <id> | prune");
            return 1;
    }
}

// ── Volumes ───────────────────────────────────────────────────────────────────

async Task<int> HandleVolumesAsync(string[] opts)
{
    var sub = opts.Length > 0 ? opts[0].ToLowerInvariant() : "ls";
    switch (sub)
    {
        case "ls":
        case "list":
        {
            var volumes = await combined.GetVolumesDetailedAsync();
            Console.WriteLine($"{"PLATFORM",-10} {"NAME",-50} SIZE");
            foreach (var vol in volumes)
                Console.WriteLine($"{vol.HostPlatform,-10} {Trunc(vol.Name, 50),-50} {vol.Size}");
            return 0;
        }
        case "rm":
        case "remove":
        {
            var name = opts.Length > 1 ? opts[1] : null;
            if (string.IsNullOrWhiteSpace(name)) { Console.WriteLine("volumes rm <name>"); return 1; }
            var volumes = await combined.GetVolumesDetailedAsync();
            var match = volumes.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is null) { Console.WriteLine($"Volume not found: {name}"); return 1; }
            var qualifiedName = $"{match.Platform}:{match.Name}";
            var ok = await combined.RemoveVolumeAsync(qualifiedName);
            Console.WriteLine(ok ? $"Removed: {match.Name}" : $"Failed to remove: {name}");
            return ok ? 0 : 1;
        }
        case "prune":
        {
            Console.Write("Remove all unused volumes from docker and podman? (y/N): ");
            if (!string.Equals(Console.ReadLine(), "y", StringComparison.OrdinalIgnoreCase)) return 0;
            var ok = await combined.PruneVolumesAsync();
            Console.WriteLine(ok ? "Done." : "Prune failed (partial).");
            return ok ? 0 : 1;
        }
        default:
            Console.WriteLine("volumes: ls | rm <name> | prune");
            return 1;
    }
}

// ── Bundles ───────────────────────────────────────────────────────────────────

async Task<int> HandleBundlesAsync(string[] opts)
{
    var sub = opts.Length > 0 ? opts[0].ToLowerInvariant() : "list";
    switch (sub)
    {
        case "ls":
        case "list":
        {
            var outputDir = CrossposeEnvironment.OutputDirectory;
            if (!Directory.Exists(outputDir)) { Console.WriteLine("No bundles."); return 0; }
            var zips = Directory.GetFiles(outputDir, "*.zip")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
            if (zips.Count == 0) { Console.WriteLine("No bundles."); return 0; }
            Console.WriteLine($"{"NAME",-60} MODIFIED");
            foreach (var z in zips)
                Console.WriteLine($"{Trunc(Path.GetFileName(z), 60),-60} {File.GetLastWriteTime(z):yyyy-MM-dd HH:mm}");
            return 0;
        }
        case "rm":
        case "remove":
        {
            var name = opts.Length > 1 ? opts[1] : null;
            if (string.IsNullOrWhiteSpace(name)) { Console.WriteLine("bundles rm <name>"); return 1; }
            var outputDir = CrossposeEnvironment.OutputDirectory;
            var target = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(outputDir, name)
                : Path.Combine(outputDir, name + ".zip");
            if (!File.Exists(target)) { Console.WriteLine($"Bundle not found: {name}"); return 1; }
            File.Delete(target);
            Console.WriteLine($"Removed: {Path.GetFileName(target)}");
            return 0;
        }
        default:
            Console.WriteLine("bundles: list | rm <name>");
            return 1;
    }
}

// ── Deployments ───────────────────────────────────────────────────────────────

async Task<int> HandleDeploymentsAsync(string[] opts)
{
    var sub = opts.Length > 0 ? opts[0].ToLowerInvariant() : "list";
    if (sub is "ls" or "list")
    {
        var deployDir = CrossposeEnvironment.DeploymentDirectory;
        if (!Directory.Exists(deployDir)) { Console.WriteLine("No deployments."); return 0; }
        var projects = Directory.GetDirectories(deployDir).OrderBy(p => p);
        var any = false;
        Console.WriteLine($"{"PROJECT / VERSION",-60} PATH");
        foreach (var proj in projects)
        {
            foreach (var ver in Directory.GetDirectories(proj).OrderByDescending(v => v))
            {
                any = true;
                var label = $"{Path.GetFileName(proj)}/{Path.GetFileName(ver)}";
                Console.WriteLine($"{Trunc(label, 60),-60} {ver}");
            }
        }
        if (!any) Console.WriteLine("No deployments.");
        return 0;
    }
    Console.WriteLine("deployments: list");
    await Task.CompletedTask;
    return 1;
}

// ── Charts ────────────────────────────────────────────────────────────────────

async Task<int> HandleChartsAsync(string[] opts)
{
    var sub = opts.Length > 0 ? opts[0].ToLowerInvariant() : "list";
    if (sub is "ls" or "list")
    {
        var chartsDir = CrossposeEnvironment.HelmChartsDirectory;
        if (!Directory.Exists(chartsDir)) { Console.WriteLine("No charts."); return 0; }
        var files = Directory.GetFiles(chartsDir)
            .OrderBy(f => f)
            .ToList();
        if (files.Count == 0) { Console.WriteLine("No charts."); return 0; }
        Console.WriteLine($"{"NAME",-60} MODIFIED");
        foreach (var f in files)
            Console.WriteLine($"{Trunc(Path.GetFileName(f), 60),-60} {File.GetLastWriteTime(f):yyyy-MM-dd HH:mm}");
        return 0;
    }
    Console.WriteLine("charts: list");
    await Task.CompletedTask;
    return 1;
}

async Task<int> RemoveDeployment(string[] opts)
{
    var q = new Queue<string>(opts);
    string? dir = null;
    while (q.Count > 0)
    {
        var t = q.Dequeue();
        if (t is "--dir" or "--directory" && q.Count > 0)
            dir = q.Dequeue();
    }
    if (string.IsNullOrWhiteSpace(dir))
    {
        Console.WriteLine("remove requires --dir <path>");
        return 1;
    }
    var fullPath = Path.GetFullPath(dir);
    if (!Directory.Exists(fullPath))
    {
        Console.WriteLine($"Directory not found: {fullPath}");
        return 1;
    }
    // Docker releases bind-mount file handles asynchronously after compose down.
    // Retry for up to 30 seconds to handle transient locks on e.g. secrets files.
    for (var attempt = 0; attempt < 10; attempt++)
    {
        try
        {
            Directory.Delete(fullPath, recursive: true);
            break;
        }
        catch (IOException) when (attempt < 9)
        {
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
    }
    Console.WriteLine($"Removed: {fullPath}");
    return 0;
}

async Task<int> DeployAsync(string[] opts)
{
    var q = new Queue<string>(opts);
    string? source = null;
    string? project = null;
    string? version = null;
    while (q.Count > 0)
    {
        var t = q.Dequeue();
        switch (t)
        {
            case "--project": project = q.Count > 0 ? q.Dequeue() : project; break;
            case "--version": version = q.Count > 0 ? q.Dequeue() : version; break;
            default:
                if (source is null && !t.StartsWith("--")) source = t;
                break;
        }
    }
    if (string.IsNullOrWhiteSpace(source))
    {
        Console.WriteLine("deploy requires a source path (zip file or directory).");
        Console.WriteLine("  crosspose deploy <bundle.zip> [--project <name>] [--version <v>]");
        return 1;
    }
    var fullSource = Path.GetFullPath(source);
    if (!File.Exists(fullSource) && !Directory.Exists(fullSource))
    {
        Console.WriteLine($"Source not found: {fullSource}");
        return 1;
    }
    if (string.IsNullOrWhiteSpace(project))
    {
        var stem = Path.GetFileNameWithoutExtension(fullSource);
        // Strip trailing -<epoch> segment: the folder name is <name>-<version>-<label>-<epoch>
        var lastHyphen = stem.LastIndexOf('-');
        project = lastHyphen > 0 && long.TryParse(stem[(lastHyphen + 1)..], out _)
            ? stem[..lastHyphen]
            : stem;
    }
    try
    {
        var deployService = new DefinitionDeploymentService();
        var result = await deployService.PrepareAsync(new DefinitionDeploymentRequest
        {
            SourcePath = fullSource,
            BaseDirectory = CrossposeEnvironment.DeploymentDirectory,
            ProjectName = project,
            Version = version
        });
        Console.WriteLine($"Deployed to: {result.TargetPath}");
        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Deploy failed.");
        Console.WriteLine($"Deploy failed: {ex.Message}");
        return 1;
    }
}

async Task<int> HandleSourcesAsync(string[] cliArgs)
{
    if (cliArgs.Length == 0)
    {
        Console.WriteLine("sources requires a subcommand: list, add, charts");
        return 1;
    }

    var sub = cliArgs[0].ToLowerInvariant();
    var rest = cliArgs.Skip(1).ToArray();
    switch (sub)
    {
        case "list":
            await ListSourcesAsync();
            return 0;
        case "add":
            return await AddSourceAsync(rest);
        case "charts":
            return await ListChartsForSourceAsync(rest);
        default:
            Console.WriteLine("Unknown sources subcommand. Use list, add, charts.");
            return 1;
    }
}

async Task ListSourcesAsync()
{
    Console.WriteLine("Configured chart sources:");
    var helmRepos = await helmClient.RepoListAsync();
    foreach (var r in helmRepos)
    {
        Console.WriteLine($"  {r.Name} (Helm) {r.Url}");
    }
    var oci = ociStore.GetAll();
    foreach (var o in oci)
    {
        Console.WriteLine($"  {o.Name} (OCI) {o.Address}");
    }
}

(string? user, string? pass) ParseUserPass(string[] opts)
{
    string? u = null;
    string? p = null;
    var q = new Queue<string>(opts);
    while (q.Count > 0)
    {
        var t = q.Dequeue();
        switch (t)
        {
            case "--user":
            case "-u":
                u = q.Count > 0 ? q.Dequeue() : u;
                break;
            case "--pass":
            case "-p":
                p = q.Count > 0 ? q.Dequeue() : p;
                break;
            default:
                break;
        }
    }
    return (u, p);
}

async Task<int> AddSourceAsync(string[] opts)
{
    if (opts.Length == 0)
    {
        Console.WriteLine("sources add <url> [--user u --pass p]");
        return 1;
    }
    var url = opts[0];
    var (user, pass) = ParseUserPass(opts.Skip(1).ToArray());
    var auth = new SourceAuth(user, pass);

    // Helm detection
    var helmSource = new HelmSourceClient(url, loggerFactory.CreateLogger<HelmSourceClient>(), helmClient);
    var helmDetect = await helmSource.DetectAsync(auth);
    logger.LogInformation("Helm detect: {Detected} {Message}", helmDetect.IsDetected, helmDetect.Message);
    if (helmDetect.IsDetected)
    {
        var addResult = await helmClient.RepoAddAsync(helmSource.SourceName, helmSource.SourceUrl, user, pass);
        if (!addResult.IsSuccess)
        {
            Console.WriteLine($"Failed to add Helm source: {addResult.StandardError}");
            return 1;
        }
        await helmClient.RepoUpdateAsync();
        Console.WriteLine($"Added Helm chart source '{helmSource.SourceName}' ({helmSource.SourceUrl})");
        return 0;
    }

    // OCI detection
    var ociSource = new OciSourceClient(url, loggerFactory.CreateLogger<OciSourceClient>());
    var ociDetect = await ociSource.DetectAsync(auth);
    logger.LogInformation("OCI detect: {Detected} {Message} RequiresAuth={RequiresAuth}", ociDetect.IsDetected, ociDetect.Message, ociDetect.RequiresAuth);
    if (ociDetect.RequiresAuth && ociSource.SourceUrl.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase))
    {
        if (!await EnsureAzureAuthCliAsync(ExtractRegistryName(ociSource.SourceUrl), auth))
        {
            Console.WriteLine(ociDetect.Message ?? "Authentication required.");
            return 1;
        }
        ociDetect = await ociSource.DetectAsync(auth);
        logger.LogInformation("OCI detect after auth: {Detected} {Message}", ociDetect.IsDetected, ociDetect.Message);
    }
    else if (ociDetect.RequiresAuth && string.IsNullOrWhiteSpace(user))
    {
        Console.WriteLine("Authentication required. Provide --user/--pass for OCI sources.");
        return 1;
    }

    if (ociDetect.IsDetected)
    {
        ociStore.AddOrUpdate(new OciRegistryEntry
        {
            Name = ociSource.SourceName,
            Address = ociSource.SourceUrl,
            Username = string.IsNullOrWhiteSpace(user) ? null : user,
            Password = string.IsNullOrWhiteSpace(pass) ? null : pass
        });
        Console.WriteLine($"Added OCI chart source '{ociSource.SourceName}' ({ociSource.SourceUrl})");
        return 0;
    }

    Console.WriteLine($"Could not detect Helm or OCI at {url}. {helmDetect.Message ?? string.Empty} {ociDetect.Message ?? string.Empty}");
    return 1;
}

async Task<int> ListChartsForSourceAsync(string[] opts)
{
    if (opts.Length == 0)
    {
        Console.WriteLine("sources charts <sourceName> [--user u --pass p]");
        return 1;
    }
    var name = opts[0];
    var (user, pass) = ParseUserPass(opts.Skip(1).ToArray());
    var auth = new SourceAuth(user, pass);

    var oci = ociStore.GetAll().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (oci is not null)
    {
        var client = new OciSourceClient(oci.Address, loggerFactory.CreateLogger<OciSourceClient>())
        {
            NameFilter = oci.Filter
        };
        var result = await client.ListAsync(auth);
        if (!result.IsSuccess)
        {
            if (oci.Address.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Auth required for Azure ACR '{name}'.");
                if (!await EnsureAzureAuthCliAsync(ExtractRegistryName(oci.Address), auth))
                {
                    Console.WriteLine(result.Message);
                    return 1;
                }
                result = await client.ListAsync(auth);
            }
            if (!result.IsSuccess)
            {
                Console.WriteLine($"Failed to list charts: {result.Message}");
                return 1;
            }
        }
        foreach (var item in result.Items) Console.WriteLine(item);
        return 0;
    }

    var helmRepos = await helmClient.RepoListAsync();
    var repo = helmRepos.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (repo is not null)
    {
        var client = new HelmSourceClient(repo.Url, loggerFactory.CreateLogger<HelmSourceClient>(), helmClient);
        var result = await client.ListAsync(auth);
        if (!result.IsSuccess)
        {
            Console.WriteLine($"Failed to list charts: {result.Message}");
            return 1;
        }
        foreach (var item in result.Items) Console.WriteLine(item);
        return 0;
    }

    Console.WriteLine($"Source '{name}' not found.");
    return 1;
}

async Task<bool> EnsureAzureAuthCliAsync(string registryName, SourceAuth auth)
{
    var azCli = new AzureCliCheck();
    var azRes = await azCli.RunAsync(processRunner, loggerFactory.CreateLogger<AzureCliCheck>(), CancellationToken.None);
    if (!azRes.IsSuccessful)
    {
        Console.Write("Azure CLI required. Run fix now? (y/N): ");
        var input = Console.ReadLine();
        if (!string.Equals(input, "y", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var fix = await azCli.FixAsync(processRunner, loggerFactory.CreateLogger<AzureCliCheck>(), CancellationToken.None);
        if (!fix.Succeeded)
        {
            Console.WriteLine($"Azure CLI fix failed: {fix.Message}");
            return false;
        }
    }

    var acrCheck = new AzureAcrAuthWinCheck(registryName);
    var acrRes = await acrCheck.RunAsync(processRunner, loggerFactory.CreateLogger<AzureAcrAuthWinCheck>(), CancellationToken.None);
    if (acrRes.IsSuccessful) return true;

    Console.Write($"Authenticate to ACR '{registryName}' now? (y/N): ");
    var acrInput = Console.ReadLine();
    if (!string.Equals(acrInput, "y", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }
    var acrFix = await acrCheck.FixAsync(processRunner, loggerFactory.CreateLogger<AzureAcrAuthWinCheck>(), CancellationToken.None);
    if (!acrFix.Succeeded)
    {
        Console.WriteLine($"ACR auth fix failed: {acrFix.Message}");
        return false;
    }
    return true;
}

static string ExtractRegistryName(string url)
{
    try
    {
        var host = new Uri(url).Host;
        return host.Split('.')[0];
    }
    catch
    {
        return url;
    }
}
