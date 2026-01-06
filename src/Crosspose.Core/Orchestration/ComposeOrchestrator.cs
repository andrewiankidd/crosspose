using System.Collections.Generic;
using System.Linq;
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

    public ComposeOrchestrator(IContainerPlatformRunner dockerRunner, IContainerPlatformRunner podmanRunner, ILoggerFactory loggerFactory)
    {
        _dockerRunner = dockerRunner;
        _podmanRunner = podmanRunner;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<ComposeOrchestrator>();
    }

    public async Task<ComposeExecutionResult> ExecuteAsync(ComposeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        using var layout = ComposeProjectLoader.Load(request.SourcePath, request.Workload);
        if (layout.WindowsFiles.Count == 0 && layout.LinuxFiles.Count == 0)
        {
            throw new InvalidOperationException($"No compose files found for workload '{request.Workload ?? "all"}' in {request.SourcePath}.");
        }

        Task<PlatformCommandResult>? dockerTask = null;
        Task<PlatformCommandResult>? podmanTask = null;

        if (layout.WindowsFiles.Count > 0)
        {
            dockerTask = RunComposeAsync(
                _dockerRunner,
                layout,
                layout.WindowsFiles,
                layout.ProjectName,
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
                layout.ProjectName,
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

        return new ComposeExecutionResult(dockerResult, podmanResult);
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

        var args = new List<string> { "compose" };
        if (platform == ComposePlatform.Podman && request.Action == ComposeAction.Up)
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

        args.Add(request.Action.ToCommand());
        if (request.Detached && request.Action == ComposeAction.Up)
        {
            args.Add("-d");
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
        var result = await runner.ExecAsync(args, env, cancellationToken).ConfigureAwait(false);
        return new PlatformCommandResult(platform == ComposePlatform.Docker ? "docker" : "podman", result);
    }

    private static string AdaptPathForRunner(IContainerPlatformRunner runner, string path)
    {
        if (!runner.BaseCommand.Equals("wsl", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.Length >= 2 && path[1] == ':')
        {
            var drive = char.ToLowerInvariant(path[0]);
            var remainder = path.Substring(2)
                .Replace('\\', '/')
                .TrimStart('/');
            return $"/mnt/{drive}/{remainder}";
        }

        return path.Replace('\\', '/');
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private async Task<IReadOnlyDictionary<string, string>?> BuildEnvironmentAsync(
        ComposeProjectLayout layout,
        ComposeExecutionRequest request,
        ComposePlatform platform,
        CancellationToken cancellationToken)
    {
        if (request.Action != ComposeAction.Up || platform != ComposePlatform.Docker)
        {
            return null;
        }

        var network = GetPreferredNetwork(layout.RootPath) ?? TryResolveNetworkFromComposeFiles(layout);
        if (string.IsNullOrWhiteSpace(network))
        {
            return null;
        }

        var gateway = await ResolveNatGatewayAsync(network, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(gateway))
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NAT_GATEWAY_IP"] = gateway
        };
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
            var runner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>());
            return await NatGatewayResolver.ResolvePreferredGatewayAddressAsync(runner, cancellationToken, network).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve NAT gateway for network {Network}", network);
            return null;
        }
    }
}
