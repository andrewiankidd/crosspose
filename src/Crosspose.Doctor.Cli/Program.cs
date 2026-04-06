using System.Reflection;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging;
using Crosspose.Doctor.Core.Checks;
using Crosspose.Doctor.Core;
using Microsoft.Extensions.Logging;

if (LaunchedOutsideShell())
{
    Console.WriteLine("This is a command line tool.");
    Console.WriteLine("You need to open cmd.exe and run it from there, or use the GUI.");
    return 1;
}

var runFixes = args.Any(a => a is "-f" or "--fix");
var settings = DoctorSettings.Load();
var enabledAdditionals = new HashSet<string>(settings.AdditionalChecks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
foreach (var key in ParseAdditionalArgs(args))
{
    enabledAdditionals.Add(key);
}
if (args.Any(a => a is "-h" or "--help" or "/?"))
{
    PrintUsage();
    return 0;
}
if (args.Any(a => a is "-v" or "--version"))
{
    PrintVersion();
    return 0;
}

using var loggerFactory = CrossposeLoggerFactory.Create(LogLevel.Information);
var logger = loggerFactory.CreateLogger("crosspose.doctor");
var runner = new ProcessRunner(loggerFactory.CreateLogger<ProcessRunner>());

if (settings.OfflineMode)
    logger.LogInformation("Offline mode active — connectivity checks (Azure CLI, ACR auth) suppressed.");

// When --fix is passed, include AutoFix checks so autoheal runs in the script/CLI path too.
// Without --fix, AutoFix checks are excluded — they depend on persistent state (cooldown
// counters) that only accumulates correctly in the long-running GUI DoctorMonitor.
var checks = Crosspose.Doctor.Core.CheckCatalog.LoadAll(enabledAdditionals, offlineMode: settings.OfflineMode)
    .Where(c => runFixes || !c.AutoFix)
    .ToList();

// Run all checks in parallel; collect results then print/fix in order.
var checkTasks = checks.Select(async check =>
{
    var checkLogger = loggerFactory.CreateLogger(check.Name);
    var result = await check.RunAsync(runner, checkLogger, CancellationToken.None);
    return (check, checkLogger, result);
}).ToList();

await Task.WhenAll(checkTasks);

var failures = 0;
foreach (var task in checkTasks)
{
    var (check, checkLogger, result) = task.Result;
    if (result.IsSuccessful)
    {
        logger.LogInformation("✔ {Name}: {Message}", check.Name, result.Message);
        continue;
    }

    failures++;
    logger.LogWarning("✖ {Name}: {Message}", check.Name, result.Message);

    if (runFixes && check.CanFix)
    {
        logger.LogInformation("Attempting fix for {Name}...", check.Name);
        var fixResult = await check.FixAsync(runner, checkLogger, CancellationToken.None);
        var prefix = fixResult.Succeeded ? "✔" : "✖";
        logger.LogInformation("{Prefix} Fix result: {Message}", prefix, fixResult.Message);
        if (!fixResult.Succeeded) failures++;
    }
}

if (failures > 0)
{
    logger.LogWarning("Doctor completed with {Count} issue(s).", failures);
    logger.LogInformation("Re-run with --fix to attempt automated remediation.");
    PersistAdditionalChanges(settings, enabledAdditionals, logger);
    return 1;
}

logger.LogInformation("All checks passed.");
PersistAdditionalChanges(settings, enabledAdditionals, logger);
return 0;

static void PrintUsage()
{
    Console.WriteLine("crosspose doctor");
    Console.WriteLine($"  Version: {GetVersion()}");
    Console.WriteLine("  --fix, -f   Attempt to fix issues (best effort).");
    Console.WriteLine("  --enable-additional <key>   Enable additional checks (e.g., azure-cli).");
    Console.WriteLine("     (alias: --enable-optional)");
    Console.WriteLine("  --help      Show this help text.");
    Console.WriteLine("  --version   Show version.");
}

static bool LaunchedOutsideShell() => !CrossposeEnvironment.IsShellAvailable;

static void PrintVersion() => Console.WriteLine(GetVersion());
static string GetVersion() => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

static IEnumerable<string> ParseAdditionalArgs(string[] args)
{
    var enabled = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] is "--enable-additional" or "--enable-optional")
        {
            if (i + 1 >= args.Length) break;
            enabled.Add(args[i + 1]);
            i++;
        }
    }
    return enabled;
}

static void PersistAdditionalChanges(DoctorSettings settings, HashSet<string> enabledAdditionals, Microsoft.Extensions.Logging.ILogger logger)
{
    var current = new HashSet<string>(settings.AdditionalChecks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    if (current.SetEquals(enabledAdditionals)) return;

    settings.AdditionalChecks = enabledAdditionals.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    DoctorSettings.Save(settings);
    logger.LogInformation("Persisted additional checks to {Path}", DoctorSettings.GetSettingsPath());
}
