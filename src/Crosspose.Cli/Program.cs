using Crosspose.Core.Configuration;
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
    case "sources":
        return await HandleSourcesAsync(remaining);
    default:
        logger.LogError("Unknown command '{Command}'. Supported commands: ps, compose, up, down, restart, stop, start, logs, top, sources.", command);
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

    static string Trunc(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..(max - 1)] + "…";
    }
}

static void PrintUsage()
{
    Console.WriteLine("crosspose usage:");
    Console.WriteLine($"  Version: {GetVersion()}");
    Console.WriteLine("  ps [--all|-a]             Show docker and podman containers side-by-side.");
    Console.WriteLine("  compose [action] [--dir <path>] [--workload <name>] [-d]  Run compose orchestration across docker + podman.");
    Console.WriteLine("     Supported actions: up, down, restart, stop, start, logs, top, ps");
    Console.WriteLine("  sources list              List configured chart sources (Helm & OCI).");
    Console.WriteLine("  sources add <url> [--user u --pass p]   Detect and add a Helm or OCI chart source.");
    Console.WriteLine("  sources charts <sourceName>             List charts from a configured source.");
    Console.WriteLine("  --help                    Show this help text.");
    Console.WriteLine("  --version, -v             Show version.");
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
