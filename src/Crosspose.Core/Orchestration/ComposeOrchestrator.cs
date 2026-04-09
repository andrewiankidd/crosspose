using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Crosspose.Core.Deployment;
using Crosspose.Core.Diagnostics;
using YamlDotNet.Serialization;
using Crosspose.Core.Networking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Core.Orchestration;

public sealed class ComposeOrchestrator
{
    private readonly IContainerPlatformRunner _dockerRunner;
    private readonly IContainerPlatformRunner _podmanRunner;
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ProcessRunner _processRunner;

    public ComposeOrchestrator(IContainerPlatformRunner dockerRunner, IContainerPlatformRunner podmanRunner, ILoggerFactory loggerFactory)
    {
        _dockerRunner = dockerRunner;
        _podmanRunner = podmanRunner;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<ComposeOrchestrator>();
        _processRunner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>());
    }

    [SupportedOSPlatform("windows")]
    public async Task<ComposeExecutionResult> ExecuteAsync(ComposeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        using var layout = ComposeProjectLoader.Load(request.SourcePath, request.Workload);
        if (layout.WindowsFiles.Count == 0 && layout.LinuxFiles.Count == 0)
        {
            throw new InvalidOperationException($"No compose files found for workload '{request.Workload ?? "all"}' in {request.SourcePath}.");
        }

        if (request.Action == ComposeAction.Up)
        {
            // Register port-proxy requirements from conversion-report.yaml so that
            // Doctor checks are available without requiring a separate Definitions deploy step.
            var requirements = PortProxyRequirementLoader.Load(request.SourcePath);
            if (requirements.Count > 0)
            {
                var keys = requirements
                    .Where(r => r.Port > 0)
                    .Select(r => Configuration.PortProxyKey.Format(r.Port, r.ConnectPort, r.Network))
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Configuration.DoctorCheckRegistrar.EnsureChecks(keys);
            }

            if (layout.WindowsFiles.Count > 0)
            {
                await PruneOrphanedDekomposNetworksAsync(cancellationToken).ConfigureAwait(false);
            }

            // Substitute ${NAT_GATEWAY_IP} in mounted configmap files so that cross-OS service
            // URLs (e.g. Linux → Windows) are resolved to the actual gateway IP at container start.
            var network = GetPreferredNetwork(layout.RootPath) ?? TryResolveNetworkFromComposeFiles(layout);
            if (!string.IsNullOrWhiteSpace(network))
            {
                var gateway = await ResolveNatGatewayAsync(network!, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(gateway))
                {
                    SubstituteNatGatewayInConfigmaps(request.SourcePath, gateway!);
                }
            }
        }

        Task<PlatformCommandResult>? dockerTask = null;
        Task<PlatformCommandResult>? podmanTask = null;

        var effectiveProjectName = request.ProjectName ?? layout.ProjectName;

        if (layout.WindowsFiles.Count > 0)
        {
            dockerTask = RunComposeAsync(
                _dockerRunner,
                layout,
                layout.WindowsFiles,
                effectiveProjectName,
                request,
                ComposePlatform.Docker,
                cancellationToken);
        }

        if (layout.LinuxFiles.Count > 0)
        {
            podmanTask = RunComposeAsync(
                _podmanRunner,
                layout,
                layout.LinuxFiles,
                effectiveProjectName,
                request,
                ComposePlatform.Podman,
                cancellationToken);
        }

        if (dockerTask is null && podmanTask is null)
        {
            throw new InvalidOperationException("No compose tasks were scheduled.");
        }

        var running = new List<Task>();
        if (dockerTask is not null) running.Add(dockerTask);
        if (podmanTask is not null) running.Add(podmanTask);

        await Task.WhenAll(running).ConfigureAwait(false);

        var dockerResult = dockerTask?.Result;
        var podmanResult = podmanTask?.Result;

        var portProxyFixRequired = false;
        if (request.Action == ComposeAction.Up)
        {
            var applyResult = await PortProxyApplicator.TryApplyAsync(
                _processRunner,
                _loggerFactory.CreateLogger("crosspose.portproxy"),
                cancellationToken).ConfigureAwait(false);

            if (applyResult.Kind == PortProxyApplyResult.ResultKind.ElevationRequired)
            {
                _logger.LogInformation(
                    "Portproxy rules need to be applied — relaunch Doctor as Administrator to configure networking.");
                portProxyFixRequired = true;
            }
            else if (applyResult.Kind == PortProxyApplyResult.ResultKind.Applied)
            {
                _logger.LogInformation("Portproxy rules applied: {Message}", applyResult.Message);
            }
            else if (applyResult.Kind == PortProxyApplyResult.ResultKind.Failure)
            {
                _logger.LogWarning("Portproxy rule application failed: {Message}", applyResult.Message);
            }
        }

        return new ComposeExecutionResult(dockerResult, podmanResult)
        {
            PortProxyFixRequired = portProxyFixRequired
        };
    }

    private async Task<PlatformCommandResult> RunComposeAsync(
        IContainerPlatformRunner runner,
        ComposeProjectLayout layout,
        IReadOnlyList<ComposeFileEntry> files,
        string projectName,
        ComposeExecutionRequest request,
        ComposePlatform platform,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0) throw new ArgumentException("No compose files were provided.", nameof(files));

        // For Podman start/restart: use "up --force-recreate -d" instead of "start"/"restart".
        // Podman rootless reuses the container's network namespace on restart, so DNS resolution
        // is stale from container creation time. Force-recreate tears down and rebuilds the
        // container, giving it a fresh network context and re-evaluating depends_on conditions.
        var podmanForceRecreate = platform == ComposePlatform.Podman
            && request.Action is ComposeAction.Start or ComposeAction.Restart;

        var args = new List<string> { "compose" };
        if (platform == ComposePlatform.Podman && (request.Action == ComposeAction.Up || podmanForceRecreate))
        {
            args.Add("--podman-run-args=--replace");
        }
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            args.Add("-p");
            args.Add(Quote(projectName));
        }

        foreach (var file in files)
        {
            args.Add("-f");
            args.Add(Quote(AdaptPathForRunner(runner, file.FullPath)));
        }

        if (podmanForceRecreate)
        {
            args.Add("up");
            args.Add("--force-recreate");
            args.Add("-d");
        }
        else
        {
            args.Add(request.Action.ToCommand());
            if (request.Detached && request.Action == ComposeAction.Up)
            {
                args.Add("-d");
            }
        }

        if (request.AdditionalArguments is not null)
        {
            foreach (var extra in request.AdditionalArguments)
            {
                args.Add(extra);
            }
        }

        _logger.LogInformation("Running {Platform} compose: {Command}", platform, string.Join(" ", args));
        var env = await BuildEnvironmentAsync(layout, request, platform, cancellationToken).ConfigureAwait(false);

        // For podman Up: run a concurrent healthcheck driver so containers with
        // service_healthy depends_on conditions can transition out of "starting".
        // crosspose-data has no systemd, so podman's healthcheck timer never fires
        // automatically — without this, compose up blocks until it times out.
        //
        // compose up -d returns immediately (detached). We keep the driver alive for
        // a grace period after compose exits so containers that were still in "created"
        // when compose returned have time to start and become healthy.
        if (platform == ComposePlatform.Podman && (request.Action == ComposeAction.Up || podmanForceRecreate))
        {
            using var hcCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var hcTask = RunPodmanHealthcheckDriverAsync(hcCts.Token);
            var composeResult = await runner.ExecAsync(args, env, cancellationToken).ConfigureAwait(false);
            hcCts.CancelAfter(TimeSpan.FromSeconds(60));
            try { await hcTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            return new PlatformCommandResult("podman", composeResult);
        }

        var result = await runner.ExecAsync(args, env, cancellationToken).ConfigureAwait(false);
        return new PlatformCommandResult(platform == ComposePlatform.Docker ? "docker" : "podman", result);
    }

    private static string AdaptPathForRunner(IContainerPlatformRunner runner, string path) =>
        runner.BaseCommand.Equals("wsl", StringComparison.OrdinalIgnoreCase)
            ? WslRunner.ToWslPath(path)
            : path;

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private async Task<IReadOnlyDictionary<string, string>?> BuildEnvironmentAsync(
        ComposeProjectLayout layout,
        ComposeExecutionRequest request,
        ComposePlatform platform,
        CancellationToken cancellationToken)
    {
        if (request.Action != ComposeAction.Up)
        {
            return null;
        }

        var network = GetPreferredNetwork(layout.RootPath) ?? TryResolveNetworkFromComposeFiles(layout);
        if (string.IsNullOrWhiteSpace(network))
        {
            return null;
        }

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var gateway = await ResolveNatGatewayAsync(network, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(gateway))
        {
            env["NAT_GATEWAY_IP"] = gateway;
        }

        // Resolve the WSL host IP for Linux→Windows communication.
        var wslHost = await Networking.WslHostResolver.ResolveAsync(_processRunner, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(wslHost))
        {
            env["WSL_HOST_IP"] = wslHost;
        }

        return env.Count > 0 ? env : null;
    }

    private static string? GetPreferredNetwork(string layoutRoot)
    {
        try
        {
            var requirements = PortProxyRequirementLoader.Load(layoutRoot);
            return requirements
                .Select(requirement => requirement.Network)
                .FirstOrDefault(network => !string.IsNullOrWhiteSpace(network));
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolveNetworkFromComposeFiles(ComposeProjectLayout layout)
    {
        if (layout.WindowsFiles.Count == 0)
        {
            return null;
        }

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        foreach (var file in layout.WindowsFiles)
        {
            if (!File.Exists(file.FullPath)) continue;
            try
            {
                var yaml = File.ReadAllText(file.FullPath);
                var doc = deserializer.Deserialize<Dictionary<object, object>>(yaml);
                if (doc is null) continue;
                if (!doc.TryGetValue("networks", out var networks)) continue;
                var name = TryGetFirstNetworkName(networks);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private static string? TryGetFirstNetworkName(object? networks)
    {
        if (networks is IDictionary<object, object> dict)
        {
            foreach (var key in dict.Keys)
            {
                if (key is string s && !string.IsNullOrWhiteSpace(s)) return s;
                if (key is not null)
                {
                    var str = key.ToString();
                    if (!string.IsNullOrWhiteSpace(str)) return str;
                }
            }
        }
        else if (networks is IDictionary<string, object?> dict2 && dict2.Count > 0)
        {
            return dict2.Keys.FirstOrDefault();
        }

        return null;
    }

    private async Task<string?> ResolveNatGatewayAsync(string network, CancellationToken cancellationToken)
    {
        try
        {
            return await NatGatewayResolver.ResolvePreferredGatewayAddressAsync(_processRunner, cancellationToken, network).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve NAT gateway for network {Network}", network);
            return null;
        }
    }

    /// <summary>
    /// Replaces the literal <c>${NAT_GATEWAY_IP}</c> placeholder in every file under
    /// <c>&lt;sourcePath&gt;/configmaps/</c> with the resolved gateway address.
    /// This is needed because bind-mounted config files are not subject to Docker/podman-compose
    /// environment variable interpolation — only compose file fields are.
    /// </summary>
    private void SubstituteNatGatewayInConfigmaps(string sourcePath, string gatewayIp)
    {
        const string placeholder = "${NAT_GATEWAY_IP}";
        var configmapsDir = System.IO.Path.Combine(sourcePath, "configmaps");
        if (!System.IO.Directory.Exists(configmapsDir)) return;

        foreach (var file in System.IO.Directory.EnumerateFiles(configmapsDir, "*", System.IO.SearchOption.AllDirectories))
        {
            try
            {
                var content = System.IO.File.ReadAllText(file);
                if (!content.Contains(placeholder, System.StringComparison.Ordinal)) continue;
                System.IO.File.WriteAllText(file, content.Replace(placeholder, gatewayIp, System.StringComparison.Ordinal));
                _logger.LogDebug("Substituted NAT_GATEWAY_IP in configmap file {File}", file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not substitute NAT_GATEWAY_IP in {File}", file);
            }
        }
    }

    /// <summary>
    /// Polls podman for containers whose healthcheck is in "starting" state and calls
    /// `podman healthcheck run` for each. Runs concurrently with `podman-compose up`
    /// to ensure service_healthy depends_on conditions are met without requiring systemd.
    /// </summary>
    private async Task RunPodmanHealthcheckDriverAsync(CancellationToken cancellationToken)
    {
        var wsl = new WslRunner(_processRunner);
        var distro = Configuration.CrossposeEnvironment.WslDistro;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

                // podman-compose 1.x creates all containers before starting any, and evaluates
                // service_healthy before starting even the dependency — deadlocking with itself.
                // Work around this by starting any Created containers so they can actually run
                // and have their healthchecks executed.
                var createdResult = await wsl.ExecAsync(
                    ["-d", distro, "--", "podman", "ps", "-a", "--filter", "status=created", "--format", "{{.Names}}"],
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (createdResult.IsSuccess)
                {
                    foreach (var name in SplitNames(createdResult.StandardOutput))
                    {
                        _logger.LogInformation("Starting Created podman container {Container}", name);
                        var startResult = await wsl.ExecAsync(
                            ["-d", distro, "--", "podman", "start", name],
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (!startResult.IsSuccess)
                        {
                            _logger.LogWarning("Failed to start container {Container}: {Error}",
                                name,
                                string.IsNullOrWhiteSpace(startResult.StandardError)
                                    ? startResult.StandardOutput
                                    : startResult.StandardError);
                        }
                    }
                }

                // Run healthchecks for any running containers still in "starting" state.
                var startingResult = await wsl.ExecAsync(
                    ["-d", distro, "--", "podman", "ps", "--filter", "health=starting", "--format", "{{.Names}}"],
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (startingResult.IsSuccess)
                {
                    foreach (var name in SplitNames(startingResult.StandardOutput))
                    {
                        _logger.LogDebug("Driving healthcheck for {Container}", name);
                        await wsl.ExecAsync(
                            ["-d", distro, "--", "podman", "healthcheck", "run", name],
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* best effort — don't interrupt compose up */ }
        }
    }

    private static IEnumerable<string> SplitNames(string output) =>
        output.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
              .Where(n => !string.IsNullOrWhiteSpace(n));

    /// <summary>
    /// Removes Docker networks that match the Dekompose naming convention (*_dekompose-*)
    /// and have no attached containers. Called before compose up to prevent HNS 0x32 errors
    /// caused by stale NAT networks accumulating from previous deployments.
    /// </summary>
    private async Task PruneOrphanedDekomposNetworksAsync(CancellationToken cancellationToken)
    {
        const string pattern = "_dekompose-";
        try
        {
            // ExecAsync joins args with spaces; quote tokens that contain spaces so Windows
            // argument parsing passes them as a single token to docker.
            var lsResult = await _dockerRunner.ExecAsync(
                new[] { "network", "ls", "--format", "{{.Name}}" },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!lsResult.IsSuccess || string.IsNullOrWhiteSpace(lsResult.StandardOutput))
            {
                return;
            }

            var candidates = lsResult.StandardOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(n => n.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var name in candidates)
            {
                // "{{len .Containers}}" contains a space — wrap in quotes so it reaches docker as one argument.
                var inspectResult = await _dockerRunner.ExecAsync(
                    new[] { "network", "inspect", "--format", "\"{{len .Containers}}\"", name },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!inspectResult.IsSuccess) continue;

                if (int.TryParse(inspectResult.StandardOutput.Trim(), out var count) && count == 0)
                {
                    _logger.LogInformation("Removing orphaned Dekompose Docker network {Network}", name);
                    await _dockerRunner.ExecAsync(
                        new[] { "network", "rm", name },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort — don't block compose up if cleanup fails
            _logger.LogWarning(ex, "Failed to prune orphaned Dekompose Docker networks");
        }
    }
}
