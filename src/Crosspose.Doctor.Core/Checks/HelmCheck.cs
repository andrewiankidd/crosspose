using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

public sealed class HelmCheck : ICheckFix
{
    public string Name => "helm";
    public string Description => "Required to fetch and render Helm charts before dekompose.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("helm", "version --short", cancellationToken: cancellationToken);
        if (result.IsSuccess)
        {
            var versionLine = result.StandardOutput.Split(Environment.NewLine).FirstOrDefault()?.Trim();
            return CheckResult.Success(versionLine ?? "helm available.");
        }

        // PATH may not be refreshed in the current process (e.g. immediately after a winget install).
        // Try known install locations before declaring failure.
        var helmExe = ResolveHelmPath();
        if (helmExe is not null)
        {
            var probe = await runner.RunAsync(helmExe, "version --short", cancellationToken: cancellationToken);
            if (probe.IsSuccess)
            {
                var versionLine = probe.StandardOutput.Split(Environment.NewLine).FirstOrDefault()?.Trim();
                return CheckResult.Success(versionLine ?? $"helm available at {helmExe}.");
            }
        }

        return CheckResult.Failure(string.IsNullOrWhiteSpace(result.StandardError)
            ? "helm not available."
            : result.StandardError);
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        if (!await WingetAvailable(runner, cancellationToken))
        {
            return FixResult.Failure("winget not available; install Helm manually from https://helm.sh/docs/intro/install/");
        }

        var result = await runner.RunAsync(
            "winget",
            "install -e --id Helm.Helm -h --source winget --accept-source-agreements --accept-package-agreements",
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
            return FixResult.Success("Helm installed via winget.");

        // winget exits non-zero when the package is already up to date — treat that as success.
        var combined = (result.StandardOutput + result.StandardError).ToLowerInvariant();
        if (combined.Contains("no available upgrade") || combined.Contains("already installed") || combined.Contains("no newer package"))
            return FixResult.Success("Helm is already installed and up to date.");

        return FixResult.Failure($"winget install Helm.Helm failed: {result.StandardError}");
    }

    private static async Task<bool> WingetAvailable(ProcessRunner runner, CancellationToken token)
    {
        var result = await runner.RunAsync("winget", "--version", cancellationToken: token);
        return result.IsSuccess;
    }

    private static string? ResolveHelmPath()
    {
        var candidates = new[]
        {
            // Official installer / winget system-scope
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Helm", "helm.exe"),
            // Chocolatey
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", "helm.exe"),
            // Scoop
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "helm.exe"),
            // User-scoped Programs
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Helm", "helm.exe"),
        };

        var direct = candidates.FirstOrDefault(File.Exists);
        if (direct is not null) return direct;

        // winget user-scoped installs land in a versioned subdirectory under WinGet\Packages.
        // Search it without needing to know the exact version.
        var wingetPackages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");

        if (Directory.Exists(wingetPackages))
        {
            foreach (var dir in Directory.GetDirectories(wingetPackages, "Helm.Helm*"))
            {
                var found = Directory.GetFiles(dir, "helm.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
        }

        // When running elevated the process PATH excludes user-level PATH additions.
        // Read the user PATH directly from the registry to catch those entries.
        try
        {
            var userPath = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Environment", "Path", null) as string;
            if (userPath is not null)
            {
                foreach (var dir in userPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var helmExe = Path.Combine(dir.Trim(), "helm.exe");
                    if (File.Exists(helmExe)) return helmExe;
                }
            }
        }
        catch
        {
            // Registry read is best-effort.
        }

        return null;
    }
}
