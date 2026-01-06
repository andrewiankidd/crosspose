using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Linq;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Crosspose.Doctor.Checks;

/// <summary>
/// Ensures a dedicated Alpine-based WSL instance exists (default name: crosspose-data).
/// </summary>
public sealed class CrossposeWslCheck : ICheckFix
{
    private const string DefaultTargetDistro = "crosspose-data";
    private const string DefaultUser = "crossposeuser";
    private const string DefaultPass = "crossposepassword";
    private const string ReleasesBaseUrl = "https://dl-cdn.alpinelinux.org/alpine/latest-stable/releases/x86_64/";
    private const string LatestReleasesManifestUrl = "https://dl-cdn.alpinelinux.org/alpine/latest-stable/releases/x86_64/latest-releases.yaml";
    private const string MiniRootTitle = "Mini root filesystem";
    private const string MiniRootFlavor = "alpine-minirootfs";
    private const string TargetArch = "x86_64";
    private const string RootFsRelativePath = @"wsl\cache\alpine-rootfs.tar";

    public string Name => "crosspose-wsl-instance";
    public string Description => "Ensures the dedicated 'crosspose-data' Alpine WSL instance exists and is reachable.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var targetDistro = GetTargetDistro();
        var user = GetUser();

        var list = await runner.RunAsync("wsl", "-l -v", cancellationToken: cancellationToken);
        if (!list.IsSuccess)
        {
            return CheckResult.Failure("Unable to query WSL distributions. Ensure WSL is enabled.");
        }

        if (!DistroExists(list.StandardOutput, targetDistro))
        {
            return CheckResult.Failure($"WSL distro '{targetDistro}' not found.");
        }

        var ping = await runner.RunAsync("wsl", $"-d {targetDistro} -- echo ok", cancellationToken: cancellationToken);
        if (!ping.IsSuccess)
        {
            return CheckResult.Failure($"WSL distro '{targetDistro}' is registered but not accessible.");
        }

        var auth = await runner.RunAsync("wsl", $"-d {targetDistro} --user {user} -- echo ok", cancellationToken: cancellationToken);
        if (!auth.IsSuccess)
        {
            return CheckResult.Failure($"WSL distro '{targetDistro}' exists but user '{user}' login failed.");
        }

        return CheckResult.Success($"WSL distro '{targetDistro}' is available and user '{user}' can log in.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var targetDistro = GetTargetDistro();
        var user = GetUser();
        var pass = GetPass();

        _ = await runner.RunAsync("wsl", $"--unregister {targetDistro}", cancellationToken: cancellationToken);

        var (rootFsPath, rootFsError) = await EnsureAlpineRootFsAsync(logger, cancellationToken);
        if (rootFsPath is null)
        {
            return FixResult.Failure($"Unable to retrieve Alpine rootfs for import. {rootFsError}");
        }

        var targetRoot = PrepareTargetDirectory(targetDistro);
        var import = await runner.RunAsync("wsl", $"--import {targetDistro} \"{targetRoot}\" \"{rootFsPath}\" --version 2", cancellationToken: cancellationToken);
        if (!import.IsSuccess)
        {
            var importError = string.IsNullOrWhiteSpace(import.StandardError)
                ? import.StandardOutput
                : import.StandardError;
            return FixResult.Failure($"Failed to import {targetDistro}: {importError}");
        }

        var ensureUser = await runner.RunAsync("wsl", $"-d {targetDistro} -- sh -c \"id -u {user} >/dev/null 2>&1 || adduser -D -s /bin/sh {user}\"", cancellationToken: cancellationToken);
        if (!ensureUser.IsSuccess)
        {
            logger.LogWarning("Unable to ensure user {User} inside {Distro}: {Error}", user, targetDistro, ensureUser.StandardError);
        }

        var setPass = await runner.RunAsync("wsl", $"-d {targetDistro} -- sh -c \"echo '{user}:{pass}' | chpasswd\"", cancellationToken: cancellationToken);
        if (!setPass.IsSuccess)
        {
            logger.LogWarning("Unable to set password for {User} inside {Distro}: {Error}", user, targetDistro, setPass.StandardError);
        }

        return FixResult.Success($"Created/updated WSL distro '{targetDistro}' with user '{user}'.");
    }

    private static bool DistroExists(string listOutput, string name)
    {
        if (string.IsNullOrWhiteSpace(listOutput)) return false;
        listOutput = listOutput.Replace("\0", string.Empty);
        var lines = listOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("NAME", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Install", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string GetTargetDistro() => CrossposeEnvironment.WslDistro;

    private static string GetUser() => CrossposeEnvironment.WslUser;

    private static string GetPass() => CrossposeEnvironment.WslPassword;

    private static async Task<(string? Path, string? Error)> EnsureAlpineRootFsAsync(ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var targetPath = AppDataLocator.GetPreferredFilePath(RootFsRelativePath);
            if (File.Exists(targetPath))
            {
                return (targetPath, null);
            }

            var downloadPath = Path.Combine(Path.GetDirectoryName(targetPath)!, "alpine-rootfs.tar.gz");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            using var http = new HttpClient();
            var (fileName, manifestError) = await GetLatestMiniRootFsFileNameAsync(http, logger, cancellationToken).ConfigureAwait(false);
            if (fileName is null)
            {
                return (null, manifestError ?? "Mini rootfs entry not found in releases manifest.");
            }

            var downloadUrl = $"{ReleasesBaseUrl}{fileName}";
            logger.LogInformation("Downloading Alpine rootfs from {Url}", downloadUrl);
            using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var download = File.Create(downloadPath);
                await responseStream.CopyToAsync(download, cancellationToken).ConfigureAwait(false);
            }

            await using (var source = File.OpenRead(downloadPath))
            await using (var gzip = new GZipStream(source, CompressionMode.Decompress))
            await using (var target = File.Create(targetPath))
            {
                await gzip.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            File.Delete(downloadPath);
            logger.LogInformation("Cached Alpine rootfs at {Path}", targetPath);
            return (targetPath, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download Alpine rootfs.");
            return (null, ex.Message);
        }
    }

    private static string PrepareTargetDirectory(string distroName)
    {
        var rootBase = AppDataLocator.GetPreferredDirectory("wsl");
        var targetRoot = Path.Combine(rootBase, distroName);
        if (Directory.Exists(targetRoot))
        {
            if (Directory.GetFileSystemEntries(targetRoot).Length > 0)
            {
                Directory.Delete(targetRoot, recursive: true);
            }
        }
        Directory.CreateDirectory(targetRoot);
        return targetRoot;
    }

    private static async Task<(string? FileName, string? Error)> GetLatestMiniRootFsFileNameAsync(HttpClient http, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(LatestReleasesManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var yaml = await reader.ReadToEndAsync().ConfigureAwait(false);

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var entries = deserializer.Deserialize<List<AlpineReleaseEntry>>(yaml);
            var match = entries?
                .FirstOrDefault(entry =>
                    (entry.Title?.Equals(MiniRootTitle, StringComparison.OrdinalIgnoreCase) == true ||
                     entry.Flavor?.Equals(MiniRootFlavor, StringComparison.OrdinalIgnoreCase) == true) &&
                    entry.Arch?.Equals(TargetArch, StringComparison.OrdinalIgnoreCase) == true &&
                    !string.IsNullOrWhiteSpace(entry.File));

            if (match?.File is null)
            {
                logger.LogWarning("Mini rootfs entry not found in Alpine releases manifest.");
                return (null, "Mini rootfs entry not found in releases manifest.");
            }

            logger.LogInformation("Latest Alpine mini rootfs release: {File}", match.File);
            return (match.File, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read Alpine releases manifest.");
            return (null, ex.Message);
        }
    }

    private sealed class AlpineReleaseEntry
    {
        [YamlMember(Alias = "title")]
        public string? Title { get; set; }

        [YamlMember(Alias = "arch")]
        public string? Arch { get; set; }

        [YamlMember(Alias = "flavor")]
        public string? Flavor { get; set; }

        [YamlMember(Alias = "file")]
        public string? File { get; set; }
    }
}
