using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Crosspose.Dekompose.Services;

public sealed class ComposeGenerator
{
    private const string NatGatewayPlaceholder = "${NAT_GATEWAY_IP}";

    private readonly ILogger<ComposeGenerator> _logger;
    private readonly Random _rand = new();
    private readonly HashSet<int> _usedPorts = new();
    private readonly HashSet<int> _usedInfraPorts = new();
    private readonly HashSet<string> _windowsServiceNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _linuxServiceNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public ComposeGenerator(ILogger<ComposeGenerator> logger)
    {
        _logger = logger;
    }

    public async Task GenerateAsync(
        string manifestPath,
        string outputDirectory,
        string networkName,
        bool includeInfra,
        bool remapServicePorts,
        IReadOnlyList<DekomposeRuleSet> ruleSets,
        CancellationToken cancellationToken)
    {
        var documents = LoadDocuments(manifestPath).ToList();
        if (!documents.Any())
        {
            _logger.LogWarning("No Kubernetes resources found in manifest {Path}", manifestPath);
            return;
        }

        var servicePortMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var converted = new List<ConvertedRecord>();
        var unconverted = new List<UnconvertedRecord>();
        var byWorkload = new Dictionary<string, Dictionary<string, List<ComposeService>>>(StringComparer.OrdinalIgnoreCase);

        var configMaps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in documents)
        {
            if (!string.Equals(GetString(doc, "kind"), "ConfigMap", StringComparison.OrdinalIgnoreCase)) continue;
            var cmName = GetString(doc, "metadata", "name");
            if (string.IsNullOrWhiteSpace(cmName)) continue;
            var data = GetMap(doc, "data");
            if (data is null) continue;
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in data)
                if (kv.Value is string val) entries[kv.Key] = val;
            configMaps[cmName] = entries;
        }

        var runtimeContext = RuleRuntimeContext.Build(ruleSets, _logger, GetNextInfraHostPort);

        // Pre-pass 1: register all Service ports (needed by later passes).
        // Pre-pass 2: classify each workload's OS — must run before VirtualService pass so aliases inherit the right OS.
        // Pre-pass 3: register VirtualService host aliases (needs ports + OS already populated).
        foreach (var doc in documents)
        {
            if (string.Equals(GetString(doc, "kind"), "Service", StringComparison.OrdinalIgnoreCase))
                RegisterServicePorts(doc, servicePortMap);
        }
        foreach (var doc in documents)
        {
            var kind = GetString(doc, "kind");
            if (!string.Equals(kind, "Deployment", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "Job", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "Rollout", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "StatefulSet", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "DaemonSet", StringComparison.OrdinalIgnoreCase))
                continue;
            RegisterWorkloadOs(doc, runtimeContext);
        }
        foreach (var doc in documents)
        {
            if (string.Equals(GetString(doc, "kind"), "VirtualService", StringComparison.OrdinalIgnoreCase))
                RegisterVirtualServiceAliases(doc, servicePortMap);
        }

        foreach (var doc in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = GetString(doc, "kind");
            var name = GetString(doc, "metadata", "name") ?? "(unknown)";

            if (string.IsNullOrWhiteSpace(kind))
            {
                unconverted.Add(new UnconvertedRecord(name, "Unknown", "Missing kind"));
                continue;
            }

            switch (kind)
            {
                case "Service":
                case "VirtualService":
                    break; // already handled in pre-passes above

                case "ConfigMap":
                    break;

                case "Deployment":
                case "Job":
                case "Rollout":
                case "StatefulSet":
                case "DaemonSet":
                    var services = ConvertWorkload(doc, kind, servicePortMap, runtimeContext, remapServicePorts, includeInfra, outputDirectory, configMaps);
                    if (services.Count == 0)
                    {
                        unconverted.Add(new UnconvertedRecord(name, kind, "No containers found"));
                        break;
                    }

                    foreach (var svc in services)
                    {
                        if (!byWorkload.TryGetValue(svc.Workload, out var osMap))
                        {
                            osMap = new Dictionary<string, List<ComposeService>>(StringComparer.OrdinalIgnoreCase);
                            byWorkload[svc.Workload] = osMap;
                        }

                        if (!osMap.TryGetValue(svc.Os, out var list))
                        {
                            list = new List<ComposeService>();
                            osMap[svc.Os] = list;
                        }

                        list.Add(svc);
                        converted.Add(new ConvertedRecord(svc.Name, kind, svc.Workload, svc.Os, $"docker-compose.{svc.Workload}.{svc.Os}.yml", svc.Name));
                    }
                    break;

                default:
                    unconverted.Add(new UnconvertedRecord(name, kind, "Unsupported kind"));
                    break;
            }
        }

        Directory.CreateDirectory(outputDirectory);

        foreach (var workloadKvp in byWorkload)
        {
            var workload = workloadKvp.Key;
            foreach (var osKvp in workloadKvp.Value)
            {
                var os = osKvp.Key;
                var osIsWindows = string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase);
                var extraHosts = BuildExtraHosts(runtimeContext, osIsWindows);
                if (extraHosts.Count > 0)
                {
                    foreach (var svc in osKvp.Value)
                    {
                        svc.ExtraHosts.AddRange(extraHosts);
                    }
                }
                var composePath = Path.Combine(outputDirectory, $"docker-compose.{workload}.{os}.yml");
                var yaml = BuildComposeYaml(osKvp.Value, networkName, osIsWindows, runtimeContext.InfraWithHealthcheck);
                await File.WriteAllTextAsync(composePath, yaml, cancellationToken);
                _logger.LogInformation("Wrote {Path} with {Count} services", composePath, osKvp.Value.Count);
            }
        }
        if (includeInfra)
        {
            await runtimeContext.EmitInfraAsync(outputDirectory, networkName, _serializer, cancellationToken);
        }

        var reportPath = Path.Combine(outputDirectory, "conversion-report.yaml");
        var report = new Dictionary<string, object?>
        {
            ["converted"] = converted,
            ["unconverted"] = unconverted
        };
        var infraSummary = runtimeContext.GetInfraSummaries();
        if (infraSummary.Count > 0)
        {
            report["infraResources"] = infraSummary;
        }

        var portProxyRequirements = runtimeContext.GetPortProxyPorts();
        if (portProxyRequirements.Count > 0)
        {
            report["portProxyRequirements"] = portProxyRequirements
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new { port = kvp.Key, connectPort = kvp.Value, network = networkName })
                .ToList();
        }

        await File.WriteAllTextAsync(reportPath, _serializer.Serialize(report), cancellationToken);

    }


        private static string DetectOs(Dictionary<string, object?> templateSpec, string workload, RuleRuntimeContext runtimeContext)
        {
            // 1. Explicit rule-set override wins (use when chart doesn't set correct nodeSelector)
            if (runtimeContext.IsWindowsWorkload(workload))
                return "windows";

            // 2. Standard nodeSelector key
            var nodeOs = GetString(templateSpec, "nodeSelector", "kubernetes.io/os");
            if (!string.IsNullOrWhiteSpace(nodeOs))
                return nodeOs.ToLowerInvariant();

            // 3. Deprecated beta label
            var betaOs = GetString(templateSpec, "nodeSelector", "beta.kubernetes.io/os");
            if (!string.IsNullOrWhiteSpace(betaOs))
                return betaOs.ToLowerInvariant();

            // 4. nodeAffinity required rules — look for kubernetes.io/os: windows
            if (IsWindowsFromNodeAffinity(templateSpec))
                return "windows";

            return "linux";
        }

        private static bool IsWindowsFromNodeAffinity(Dictionary<string, object?> templateSpec)
        {
            if (!templateSpec.TryGetValue("affinity", out var affinityObj) || affinityObj is not Dictionary<string, object?> affinity)
                return false;
            if (!affinity.TryGetValue("nodeAffinity", out var naObj) || naObj is not Dictionary<string, object?> nodeAffinity)
                return false;
            if (!nodeAffinity.TryGetValue("requiredDuringSchedulingIgnoredDuringExecution", out var reqObj) || reqObj is not Dictionary<string, object?> required)
                return false;
            if (!required.TryGetValue("nodeSelectorTerms", out var termsObj) || termsObj is not List<object?> terms)
                return false;

            foreach (var termObj in terms)
            {
                if (termObj is not Dictionary<string, object?> term) continue;
                if (!term.TryGetValue("matchExpressions", out var exprsObj) || exprsObj is not List<object?> exprs) continue;
                foreach (var exprObj in exprs)
                {
                    if (exprObj is not Dictionary<string, object?> expr) continue;
                    var key = GetString(expr, "key");
                    if (!string.Equals(key, "kubernetes.io/os", StringComparison.OrdinalIgnoreCase)) continue;
                    var op = GetString(expr, "operator");
                    if (!string.Equals(op, "In", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!expr.TryGetValue("values", out var valsObj) || valsObj is not List<object?> vals) continue;
                    if (vals.Any(v => string.Equals(v?.ToString(), "windows", StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            return false;
        }

        private List<ComposeService> ConvertWorkload(
            Dictionary<string, object?> doc,
            string kind,
            Dictionary<string, int> servicePortMap,
            RuleRuntimeContext runtimeContext,
            bool remapServicePorts,
            bool includeInfra,
            string outputDirectory,
            Dictionary<string, Dictionary<string, string>> configMaps)
        {
            var list = new List<ComposeService>();
            var name = GetString(doc, "metadata", "name") ?? "workload";
        var workload = name.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "jobs";

        var template = GetMap(doc, "spec", "template", "spec");
        if (template is null) return list;

        var os = DetectOs(template, workload, runtimeContext);
        var volumes = ExtractVolumeDefinitions(template);

        var isCrossposeWorkload = string.Equals(workload, "crosspose", StringComparison.OrdinalIgnoreCase);
        var serviceWorkloadKey = isCrossposeWorkload ? "jobs" : workload;

        var containers = GetSequence(template, "containers");
        if (containers is null) return list;

        foreach (var item in containers)
        {
            if (item is not Dictionary<string, object?> container) continue;
            var containerName = GetString(container, "name") ?? name;
            var svcName = BuildServiceName(serviceWorkloadKey, containerName, isCrossposeWorkload);
            var image = GetString(container, "image") ?? "unknown";

            var ports = new List<string>();
            var portSeq = GetSequence(container, "ports");
            if (portSeq is not null)
            {
                foreach (var p in portSeq)
                {
                    if (p is not Dictionary<string, object?> portMap) continue;
                    var containerPort = GetInt(portMap, "containerPort");
                    if (containerPort.HasValue)
                    {
                        // Reuse the host port already reserved by RegisterServicePorts so that
                        // the compose port, the servicePortMap, and any VirtualService aliases all
                        // agree on the same host port for this service.
                        // Priority: existing svcName entry (K8s Service "erp-api") →
                        //           workload-level entry (K8s Service "erp") → allocate new.
                        int hostPort;
                        if (servicePortMap.TryGetValue(svcName, out var existingPort))
                            hostPort = existingPort;
                        else if (servicePortMap.TryGetValue(serviceWorkloadKey, out var workloadPort))
                            hostPort = workloadPort;
                        else
                            hostPort = GetNextHostPort();
                        ports.Add($"{hostPort}:{containerPort.Value}");
                        servicePortMap[svcName] = hostPort;
                        // Keep the workload-level entry in sync so VirtualService aliases resolve correctly.
                        servicePortMap[serviceWorkloadKey] = hostPort;
                    }
                }
            }

            var (env, infraDependencies) = BuildEnvironment(container, servicePortMap, runtimeContext, remapServicePorts, includeInfra, os);
            var service = new ComposeService
            {
                Name = svcName,
                Workload = serviceWorkloadKey,
                Os = os,
                Image = image,
                Ports = ports,
                Environment = env,
                Restart = "on-failure",
                IsJob = string.Equals(kind, "Job", StringComparison.OrdinalIgnoreCase),
                Healthcheck = TranslateProbe(GetMap(container, "livenessProbe") ?? GetMap(container, "readinessProbe"), os)
            };
            var volumeMounts = BuildVolumes(container, volumes, runtimeContext, outputDirectory, os, configMaps, servicePortMap, remapServicePorts, service.NamedVolumeRefs, serviceWorkloadKey);
            service.Volumes.AddRange(volumeMounts);
            if (string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase))
                _windowsServiceNames.Add(service.Name);
            else
                _linuxServiceNames.Add(service.Name);
            foreach (var infra in infraDependencies)
                service.DependsOn.Add(infra);
            list.Add(service);
        }

        // Process initContainers — emit as run-once services that the main containers depend on.
        var initContainers = GetSequence(template, "initContainers");
        if (initContainers is not null)
        {
            foreach (var item in initContainers)
            {
                if (item is not Dictionary<string, object?> container) continue;
                var containerName = GetString(container, "name") ?? "init";
                var svcName = BuildServiceName(serviceWorkloadKey, containerName, false);
                var image = GetString(container, "image") ?? "unknown";

                // Extract command and args so the init container runs its intended entrypoint,
                // not the image's default CMD (e.g. the cp to populate the shared volume).
                var cmdSeq = GetSequence(container, "command");
                var argsSeq = GetSequence(container, "args");
                var command = cmdSeq?.OfType<string>().ToList();
                List<string>? args = null;
                if (argsSeq is not null)
                {
                    // K8s args may be a sequence of strings or a multi-line scalar joined into one element.
                    var rawArgs = argsSeq.OfType<string>().ToList();
                    args = rawArgs.Count > 0 ? rawArgs : null;
                }

                var initService = new ComposeService
                {
                    Name = svcName,
                    Workload = serviceWorkloadKey,
                    Os = os,
                    Image = image,
                    Restart = null, // omit restart key — "no" serialises to YAML bool false which podman rejects
                    IsJob = true,
                    IsInitContainer = true,
                    Command = command,
                    Args = args
                };
                var volumeMounts = BuildVolumes(container, volumes, runtimeContext, outputDirectory, os, configMaps, servicePortMap, remapServicePorts, initService.NamedVolumeRefs, serviceWorkloadKey);
                initService.Volumes.AddRange(volumeMounts);
                if (string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase))
                    _windowsServiceNames.Add(initService.Name);
                else
                    _linuxServiceNames.Add(initService.Name);
                list.Add(initService);
            }
        }

        return list;
    }

        private (Dictionary<string, string> Environment, HashSet<string> InfraDependencies) BuildEnvironment(
            Dictionary<string, object?> container,
            Dictionary<string, int> servicePortMap,
            RuleRuntimeContext runtimeContext,
            bool remapServicePorts,
            bool includeInfra,
            string os)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var infraDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var envSeq = GetSequence(container, "env");
            if (envSeq is null) return (env, infraDependencies);
            var serviceIsWindows = string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase);

            foreach (var e in envSeq)
            {
                if (e is not Dictionary<string, object?> map) continue;
                var name = GetString(map, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                string? resolved = null;
                var literalValue = GetString(map, "value");
                if (!string.IsNullOrWhiteSpace(literalValue))
                {
                    if (includeInfra)
                    {
                        foreach (var infra in runtimeContext.GetReferencedInfraNames(literalValue!))
                        {
                            if (runtimeContext.IsInfraCompatible(infra, serviceIsWindows))
                            {
                                infraDependencies.Add(infra);
                            }
                        }
                    }
                    resolved = remapServicePorts
                        ? RemapServiceUrls(literalValue!, servicePortMap, runtimeContext, os)
                        : literalValue;
                    resolved = runtimeContext.Detokenize(resolved, os);
                }
                else if (map.TryGetValue("valueFrom", out var vfObj) && vfObj is Dictionary<string, object?> vfMap)
                {
                    var (resolvedFromSecret, secretInfra) = ResolveValueFrom(vfMap, runtimeContext, os);
                    resolved = resolvedFromSecret;
                    if (includeInfra)
                    {
                        foreach (var infra in secretInfra)
                        {
                            if (runtimeContext.IsInfraCompatible(infra, serviceIsWindows))
                            {
                                infraDependencies.Add(infra);
                            }
                        }
                    }
                }

            if (resolved is not null)
            {
                env[name!] = RewriteLoopbackHosts(resolved, os);
            }
        }

        return (env, infraDependencies);
    }

    private (string? Value, IReadOnlyCollection<string> InfraDependencies) ResolveValueFrom(Dictionary<string, object?> valueFrom, RuleRuntimeContext runtimeContext, string os)
    {
        if (valueFrom.TryGetValue("secretKeyRef", out var secretRefObj) && secretRefObj is Dictionary<string, object?> secretRef)
        {
            var secretName = GetString(secretRef, "name");
            var key = GetString(secretRef, "key");
            return runtimeContext.ResolveSecretValue(secretName, key, os);
        }

        return (null, Array.Empty<string>());
    }

        private List<string> BuildVolumes(
            Dictionary<string, object?> container,
            Dictionary<string, Dictionary<string, object?>> volumes,
            RuleRuntimeContext runtimeContext,
            string outputDirectory,
            string os,
            Dictionary<string, Dictionary<string, string>> configMaps,
            Dictionary<string, int>? servicePortMap = null,
            bool remapServicePorts = false,
            HashSet<string>? namedVolumeRefs = null,
            string workload = "")
        {
        var mounts = new List<string>();
        var vmSeq = GetSequence(container, "volumeMounts");
        if (vmSeq is null || volumes.Count == 0) return mounts;

        foreach (var vm in vmSeq)
        {
            if (vm is not Dictionary<string, object?> mount) continue;
            var name = GetString(mount, "name");
            var mountPath = GetString(mount, "mountPath");
            var ro = GetBool(mount, "readOnly") ?? false;
            var subPath = GetString(mount, "subPath");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mountPath)) continue;

            if (!volumes.TryGetValue(name!, out var volMap))
            {
                continue;
            }

            if (volMap.ContainsKey("configMap"))
            {
                var cm = GetMap(volMap, "configMap");
                var cmName = GetString(cm ?? new(), "name") ?? name!;
                var mode = ro ? "ro" : "rw";
                var hostPath = FormatHostPath(Path.Combine("configmaps", cmName), os);
                var containerPath = AdjustContainerPathForOs(mountPath!, os);
                EnsureHostPathExists(outputDirectory, hostPath);
                if (configMaps.TryGetValue(cmName, out var cmData))
                {
                    var trimmed = hostPath.TrimStart('.', '\\', '/').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    var absDir = Path.Combine(outputDirectory, trimmed);
                    foreach (var (key, rawValue) in cmData)
                    {
                        var value = remapServicePorts && servicePortMap is not null
                            ? RemapServiceUrls(rawValue, servicePortMap, runtimeContext, os)
                            : rawValue;
                        File.WriteAllText(Path.Combine(absDir, key), value);
                    }
                }
                mounts.Add($"{hostPath}:{containerPath}:{mode}");
                continue;
            }

            if (volMap.ContainsKey("secret"))
            {
                var secretMap = GetMap(volMap, "secret") ?? new Dictionary<string, object?>();
                var secretName = GetString(secretMap, "secretName") ?? GetString(volMap, "secretName");
                var materialized = runtimeContext.ResolveSecretFile(secretName, outputDirectory);
                if (materialized is null) continue;

                var hostPath = string.IsNullOrWhiteSpace(subPath)
                    ? FormatHostPath(materialized.RelativeDirectory, os)
                    : FormatHostPath(materialized.RelativeDataFilePath ?? materialized.RelativeFilePath, os);
                var containerPath = string.IsNullOrWhiteSpace(subPath)
                    ? mountPath!
                    : BuildContainerPath(mountPath!, subPath);
                containerPath = AdjustContainerPathForOs(containerPath, os);
                var mode = ro ? "ro" : "rw";
                mounts.Add($"{hostPath}:{containerPath}:{mode}");
                continue;
            }

            if (volMap.ContainsKey("emptyDir"))
            {
                // emptyDir volumes are shared between init containers and the main container within
                // a Pod. In compose there's no equivalent — map to a named volume so init + main
                // services can share the data across containers.
                var namedVol = string.IsNullOrWhiteSpace(workload) ? name! : $"{workload}-{name}";
                var containerPath = AdjustContainerPathForOs(mountPath!, os);
                var mode = ro ? "ro" : "rw";
                mounts.Add($"{namedVol}:{containerPath}:{mode}");
                namedVolumeRefs?.Add(namedVol);
                continue;
            }
        }

        return mounts;
    }

    private static Dictionary<string, Dictionary<string, object?>> ExtractVolumeDefinitions(Dictionary<string, object?> template)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        var volumeSeq = GetSequence(template, "volumes");
        if (volumeSeq is null) return result;

        foreach (var volume in volumeSeq)
        {
            if (volume is not Dictionary<string, object?> volMap) continue;
            var name = GetString(volMap, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            result[name!] = volMap;
        }

        return result;
    }

    private static string BuildContainerPath(string mountPath, string? subPath)
    {
        if (string.IsNullOrWhiteSpace(subPath)) return mountPath;
        if (mountPath.EndsWith("/") || mountPath.EndsWith("\\")) return mountPath + subPath;
        var separator = mountPath.Contains('\\') ? "\\" : "/";
        return $"{mountPath}{separator}{subPath}";
    }

    private static string FormatHostPath(string relativePath, string os)
    {
        var trimmed = relativePath.TrimStart('.', '\\', '/');
        if (string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase))
        {
            var windowsPath = trimmed.Replace('/', '\\');
            return $".\\{windowsPath}";
        }

        var posixPath = trimmed.Replace('\\', '/');
        return $"./{posixPath}";
    }

    private static void EnsureHostPathExists(string outputDirectory, string hostPath)
    {
        if (string.IsNullOrWhiteSpace(hostPath)) return;

        if (Path.IsPathRooted(hostPath))
        {
            Directory.CreateDirectory(hostPath);
            return;
        }

        var trimmed = hostPath.TrimStart('.', '\\', '/');
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        var normalized = trimmed.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var absolute = Path.Combine(outputDirectory, normalized);
        Directory.CreateDirectory(absolute);
    }

    private static string AdjustContainerPathForOs(string containerPath, string os)
    {
        if (!string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase))
        {
            return containerPath;
        }

        if (string.IsNullOrWhiteSpace(containerPath)) return containerPath;
        if (containerPath.Contains(':')) return containerPath;
        var normalized = containerPath.Replace('\\', '/');
        if (!normalized.StartsWith("/"))
        {
            normalized = "/" + normalized;
        }
        return $"C:{normalized}";
    }

    private static string BuildSecretRelativeDirectory(string secretName)
    {
        var safe = SanitizeSegment(secretName, "secret");
        return Path.Combine("secrets", safe);
    }

    private static string SanitizeSegment(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = candidate.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(buffer);
    }

    /// <summary>
    /// Pre-pass: determines whether a workload runs on Windows or Linux and records every
    /// service name it would produce into <see cref="_windowsServiceNames"/> or
    /// <see cref="_linuxServiceNames"/>. Must run before any <c>RemapServiceUrls</c> call
    /// so cross-OS URL rewrites use the correct host.
    /// </summary>
    private void RegisterWorkloadOs(Dictionary<string, object?> doc, RuleRuntimeContext runtimeContext)
    {
        var name = GetString(doc, "metadata", "name") ?? "workload";
        var workload = name.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "jobs";
        var template = GetMap(doc, "spec", "template", "spec");
        if (template is null) return;

        var os = DetectOs(template, workload, runtimeContext);
        var isCrossposeWorkload = string.Equals(workload, "crosspose", StringComparison.OrdinalIgnoreCase);
        var serviceWorkloadKey = isCrossposeWorkload ? "jobs" : workload;

        foreach (var seqKey in new[] { "containers", "initContainers" })
        {
            var containers = GetSequence(template, seqKey);
            if (containers is null) continue;
            foreach (var item in containers)
            {
                if (item is not Dictionary<string, object?> container) continue;
                var containerName = GetString(container, "name") ?? name;
                var svcName = BuildServiceName(serviceWorkloadKey, containerName, isCrossposeWorkload);
                if (string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase))
                    _windowsServiceNames.Add(svcName);
                else
                    _linuxServiceNames.Add(svcName);
            }
        }
    }

    private void RegisterServicePorts(Dictionary<string, object?> doc, Dictionary<string, int> servicePortMap)
    {
        var name = GetString(doc, "metadata", "name");
        if (string.IsNullOrWhiteSpace(name)) return;

        var ports = GetSequence(doc, "spec", "ports");
        if (ports is null) return;

        foreach (var p in ports)
        {
            if (p is not Dictionary<string, object?> portMap) continue;
            var port = GetInt(portMap, "port");
            if (!port.HasValue) continue;

            var host = GetNextHostPort();
            servicePortMap[name!] = host;
        }
    }

    /// <summary>
    /// Reads a VirtualService and registers each host as an alias pointing to the same
    /// host port as the destination k8s service. This lets RemapServiceUrls resolve URLs
    /// like https://local-dev-svc-billing.example.com to localhost:&lt;port&gt; even though
    /// the service port map key is "erp-api", not "billing".
    /// </summary>
    private void RegisterVirtualServiceAliases(Dictionary<string, object?> doc, Dictionary<string, int> servicePortMap)
    {
        var hosts = GetSequence(doc, "spec", "hosts");
        if (hosts is null) return;

        // Get the destination service name from the first http route
        var http = GetSequence(doc, "spec", "http");
        if (http is null) return;
        string? destService = null;
        foreach (var route in http)
        {
            if (route is not Dictionary<string, object?> routeMap) continue;
            var routeSeq = GetSequence(routeMap, "route");
            if (routeSeq is null) continue;
            foreach (var r in routeSeq)
            {
                if (r is not Dictionary<string, object?> rm) continue;
                destService = GetString(GetMap(rm, "destination") ?? [], "host");
                if (!string.IsNullOrWhiteSpace(destService)) break;
            }
            if (!string.IsNullOrWhiteSpace(destService)) break;
        }

        if (string.IsNullOrWhiteSpace(destService)) return;
        if (!servicePortMap.TryGetValue(destService!, out var port)) return;

        // Determine OS of the destination service so aliases inherit the same OS classification.
        var destIsWindows = _windowsServiceNames.Contains(destService!);

        // Register each VirtualService host as an alias for the destination port.
        // Extract just the subdomain prefix (before the first dot) as the alias key
        // so RemapServiceUrls can match it via the svc-<name> extraction.
        foreach (var hostObj in hosts)
        {
            var host = hostObj as string ?? hostObj?.ToString();
            if (string.IsNullOrWhiteSpace(host)) continue;
            var subdomain = host!.Split('.')[0]; // e.g. "local-dev-svc-billing"
            // Extract the svc-<name> portion that RemapServiceUrls captures
            var svcIdx = subdomain.IndexOf("svc-", StringComparison.OrdinalIgnoreCase);
            if (svcIdx >= 0)
            {
                var svcName = subdomain[(svcIdx + 4)..]; // e.g. "billing"
                if (!string.IsNullOrWhiteSpace(svcName) && !servicePortMap.ContainsKey(svcName))
                {
                    servicePortMap[svcName] = port;
                    // Propagate OS so HostFor() resolves cross-OS aliases correctly.
                    if (destIsWindows) _windowsServiceNames.Add(svcName);
                    else _linuxServiceNames.Add(svcName);
                }
            }
        }
    }

    private string BuildComposeYaml(List<ComposeService> services, string networkName, bool isWindows, HashSet<string>? infraWithHealthcheck = null)
    {
        var jobNames = services
            .Where(s => s.IsJob && !s.IsInitContainer)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var initContainerNames = services
            .Where(s => s.IsInitContainer)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var svcMap = new Dictionary<string, object?>();
        foreach (var svc in services)
        {
            var definition = new Dictionary<string, object?>
            {
                ["image"] = svc.Image,
                ["networks"] = new[] { networkName }
            };
            if (!string.IsNullOrWhiteSpace(svc.Restart))
            {
                definition["restart"] = svc.Restart;
            }

            // K8s command → compose entrypoint; K8s args → compose command.
            if (svc.Command is { Count: > 0 })
                definition["entrypoint"] = svc.Command;
            if (svc.Args is { Count: > 0 })
                definition["command"] = svc.Args;

            if (svc.Ports.Any())
            {
                definition["ports"] = svc.Ports;
            }

            if (svc.Environment.Any())
            {
                definition["environment"] = svc.Environment;
            }

            var depMap = new Dictionary<string, object?>();
            // Non-job services depend on job services (schema migrations etc.) in the same file.
            // service_completed_successfully is not reliably honoured by podman-compose 1.x — it can
            // leave dependents stuck in "created" forever. Use service_started + restart:on-failure
            // on the app service to recover if the job hasn't finished yet.
            if (!svc.IsJob)
            {
                foreach (var job in jobNames)
                    depMap[job] = new Dictionary<string, object?> { ["condition"] = "service_started" };
            }
            // All services (including other jobs) depend on init containers starting first.
            // service_completed_successfully is not reliably honoured by podman-compose 1.x —
            // use service_started like schema migration jobs; restart:on-failure on the main
            // service handles retries if init hasn't finished populating the shared volume yet.
            if (!svc.IsInitContainer)
            {
                foreach (var init in initContainerNames.Where(n => !depMap.ContainsKey(n)))
                    depMap[init] = new Dictionary<string, object?> { ["condition"] = "service_started" };
            }
            // Infra dependencies use service_healthy when the infra service declares a healthcheck,
            // otherwise service_started. This relies on the healthcheck being correctly configured
            // (e.g. mssql uses /opt/mssql-tools18/bin/sqlcmd so the check actually passes).
            foreach (var dep in svc.DependsOn.Where(d => !depMap.ContainsKey(d)))
            {
                var condition = infraWithHealthcheck?.Contains(dep) == true ? "service_healthy" : "service_started";
                depMap[dep] = new Dictionary<string, object?> { ["condition"] = condition };
            }
            if (depMap.Count > 0)
            {
                definition["depends_on"] = depMap;
            }

            var volumes = new List<string>(svc.Volumes);

            if (volumes.Any())
            {
                definition["volumes"] = volumes;
            }

            if (svc.Healthcheck is not null && svc.Healthcheck.Count > 0)
            {
                definition["healthcheck"] = svc.Healthcheck;
            }

            if (svc.ExtraHosts.Any())
            {
                definition["extra_hosts"] = svc.ExtraHosts;
            }

            svcMap[svc.Name] = definition;
        }

        // Windows containers run on the host HNS NAT stack. Creating additional NAT
        // networks alongside the default "nat" fails with HNS 0x32 ("not supported").
        // Reference the existing "nat" network as external so no new network is created.
        object? networkDefinition = isWindows
            ? new Dictionary<string, object?> { ["external"] = true, ["name"] = "nat" }
            : (object?)new { };

        // Collect named volumes (from emptyDir) declared across all services.
        var allNamedVolumes = services
            .SelectMany(s => s.NamedVolumeRefs)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allNamedVolumes.Count > 0)
        {
            var namedVolumesMap = allNamedVolumes.ToDictionary(v => v, _ => (object?)null);
            var compose = new
            {
                services = svcMap,
                networks = new Dictionary<string, object?> { { networkName, networkDefinition } },
                volumes = namedVolumesMap
            };
            return _serializer.Serialize(compose);
        }

        var composeNoVolumes = new
        {
            services = svcMap,
            networks = new Dictionary<string, object?> { { networkName, networkDefinition } }
        };

        return _serializer.Serialize(composeNoVolumes);
    }

    private static string BuildServiceName(string workload, string containerName, bool skipWorkloadPrefix = false)
    {
        if (string.IsNullOrWhiteSpace(workload)) return containerName;
        var prefix = workload + "-";
        if (skipWorkloadPrefix)
        {
            const string crossposePrefix = "crosspose-";
            if (containerName.StartsWith(crossposePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return containerName[crossposePrefix.Length..];
            }
            return containerName;
        }

        if (containerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return containerName;
        }

        return $"{workload}-{containerName}";
    }

        private string RewriteLoopbackHosts(string value, string os)
        {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (!string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase)) return value;

        var result = ReplaceHostAlias(value, "host.docker.internal");
        result = ReplaceHostAlias(result, "localhost");
        result = ReplaceHostAlias(result, "127.0.0.1");
        return result;
        }

        private string ReplaceHostAlias(string value, string alias)
        {
            var pattern = $"(?<![A-Za-z0-9._-]){Regex.Escape(alias)}(?![A-Za-z0-9._-])";
            return Regex.Replace(value, pattern, NatGatewayPlaceholder, RegexOptions.IgnoreCase);
        }

    private string RemapServiceUrls(string value, Dictionary<string, int> servicePortMap, RuleRuntimeContext runtimeContext, string callerOs = "linux")
    {
        var callerIsLinux = !string.Equals(callerOs, "windows", StringComparison.OrdinalIgnoreCase);

        // Choose host based on whether the target service is on a different OS from the caller.
        // Linux caller → Windows target: use NAT gateway (localhost doesn't cross the WSL boundary).
        // Windows caller → Linux target: use NAT gateway (rewriteLoopbackHosts will also catch this but
        //                                explicit here avoids relying on post-processing).
        // Same-OS: localhost is correct.
        string HostFor(string svc) =>
            (_windowsServiceNames.Contains(svc) && callerIsLinux) ||
            (_linuxServiceNames.Contains(svc) && !callerIsLinux)
                ? NatGatewayPlaceholder
                : "localhost";

        // Rewrite in-cluster Kubernetes service URLs: <svc>.default.svc.cluster.local → localhost:<port>
        // These URLs are cluster-internal and never reachable outside k8s — blank them if not running locally.
        var clusterRegex = new Regex("(?:https?://)?(?<svc>[A-Za-z0-9-]+)\\.default\\.svc\\.cluster\\.local(?::(?<port>\\d+))?(?<path>/[^\\s\"']*)?", RegexOptions.IgnoreCase);
        value = clusterRegex.Replace(value, m =>
        {
            var svc = m.Groups["svc"].Value;
            if (servicePortMap.TryGetValue(svc, out var hostPort))
                return $"http://{HostFor(svc)}:{hostPort}{m.Groups["path"].Value}";
            return "http://not-deployed";
        });

        // Rewrite external service root URLs matching configured glob patterns (local-service-match).
        // Glob supports a single * in the subdomain: e.g. "local-*.example.com".
        // The * portion must contain "svc-<name>" from which the service name is extracted.
        // If found in the local port map → http://localhost:<port>. If not (disabled in values) → blank.
        foreach (var pattern in runtimeContext.LocalServicePatterns)
        {
            var svcRegex = BuildServiceGlobRegex(pattern);
            if (svcRegex is null) continue;
            value = svcRegex.Replace(value, m =>
            {
                var svc = m.Groups["svc"].Value;
                if (servicePortMap.TryGetValue(svc, out var hostPort))
                {
                    var path = m.Groups["path"].Value;
                    return $"http://{HostFor(svc)}:{hostPort}{path}";
                }
                return "http://not-deployed";
            });
        }

        // Apply explicit url-overrides from config (case-insensitive substring replace).
        foreach (var (from, to) in runtimeContext.UrlOverrides)
        {
            var idx = value.IndexOf(from, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                value = value[..idx] + to + value[(idx + from.Length)..];
        }

        return value;
    }

    /// <summary>
    /// Converts a glob hostname pattern (e.g. <c>local-*.example.com</c>) into a regex
    /// that matches the full service URL and captures the service name from the <c>svc-&lt;name&gt;</c>
    /// segment within the wildcard portion.
    /// </summary>
    private static Regex? BuildServiceGlobRegex(string pattern)
    {
        var star = pattern.IndexOf('*');
        if (star < 0)
        {
            // No wildcard — treat the whole string as a domain suffix.
            var escaped = Regex.Escape(pattern.TrimStart('.'));
            return new Regex(
                $@"https?://(?:[A-Za-z0-9-]+-)?svc-(?<svc>[A-Za-z0-9-]+)\.{escaped}(?<path>/[^\s""']*)?",
                RegexOptions.IgnoreCase);
        }

        // Split around the single *.
        var prefix = Regex.Escape(pattern[..star]);
        var suffix = Regex.Escape(pattern[(star + 1)..]);
        // The * matches the environment prefix + "svc-<name>", e.g. "dev-svc-core".
        return new Regex(
            $@"https?://{prefix}(?:[A-Za-z0-9-]+-)?svc-(?<svc>[A-Za-z0-9-]+){suffix}(?<path>/[^\s""']*)?",
            RegexOptions.IgnoreCase);
    }

    private int GetNextHostPort()
    {
        const int min = 60000;
        const int max = 65000;
        int port;
        do
        {
            port = _rand.Next(min, max);
        } while (!_usedPorts.Add(port));
        return port;
    }

    private int GetNextInfraHostPort()
    {
        const int min = 40000;
        const int max = 50000;
        int port;
        do
        {
            port = _rand.Next(min, max);
        } while (!_usedInfraPorts.Add(port) || _usedPorts.Contains(port));
        return port;
    }

    private IEnumerable<Dictionary<string, object?>> LoadDocuments(string path)
    {
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        var content = File.ReadAllText(path);
        var docs = Regex.Split(content, @"^---\s*$", RegexOptions.Multiline);

        foreach (var doc in docs)
        {
            if (string.IsNullOrWhiteSpace(doc)) continue;
            Dictionary<string, object?>? normalized = null;
            try
            {
                var raw = deserializer.Deserialize<Dictionary<object, object?>>(doc);
                normalized = Normalize(raw) as Dictionary<string, object?>;
            }
            catch (YamlException ex)
            {
                _logger.LogWarning(ex, "Skipping YAML document in {Path}", path);
            }

            if (normalized is not null)
            {
                yield return normalized;
            }
        }
    }

    private static object? Normalize(object? value)
    {
        // YamlDotNet deserializes nested mappings as Dictionary<object, object> (non-nullable),
        // while the top-level type is Dictionary<object, object?> (nullable). Due to generic
        // invariance, IDictionary<object, object?> only matches the top-level. We must also
        // handle IDictionary<object, object> for nested maps, otherwise keys like
        // "kubernetes.io/os" inside nodeSelector are never converted to Dictionary<string, object?>
        // and GetNode silently fails to find them.
        if (value is IDictionary<object, object?> mapNullable)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in mapNullable)
                dict[kvp.Key?.ToString() ?? string.Empty] = Normalize(kvp.Value);
            return dict;
        }
        if (value is IDictionary<object, object> mapNonNullable)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in mapNonNullable)
                dict[kvp.Key?.ToString() ?? string.Empty] = Normalize(kvp.Value);
            return dict;
        }
        if (value is IEnumerable<object?> seqNullable && value is not string)
            return seqNullable.Select(Normalize).ToList();
        if (value is IEnumerable<object> seqNonNullable && value is not string)
            return seqNonNullable.Select(x => Normalize(x)).ToList();
        return value;
    }

    private static object? ConvertNode(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                return scalar.Value;
            case YamlMappingNode map:
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in map.Children)
                {
                    if (entry.Key is YamlScalarNode key)
                    {
                        dict[key.Value ?? string.Empty] = ConvertNode(entry.Value);
                    }
                }
                return dict;
            case YamlSequenceNode seq:
                return seq.Children.Select(ConvertNode).ToList();
            default:
                return null;
        }
    }

    /// <summary>
    /// Translates a Kubernetes liveness or readiness probe into a Docker Compose healthcheck block.
    /// Returns null if the probe type is not supported or the probe map is missing.
    /// Supported: exec, httpGet (Linux: bash /dev/tcp, Windows: Invoke-WebRequest), tcpSocket (Linux only).
    /// </summary>
    private static Dictionary<string, object?>? TranslateProbe(Dictionary<string, object?>? probe, string os)
    {
        if (probe is null) return null;
        var isWindows = string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase);

        List<object?>? test = null;

        var exec = GetMap(probe, "exec");
        if (exec is not null)
        {
            var cmd = GetSequence(exec, "command");
            if (cmd is not null && cmd.Count > 0)
            {
                var args = new List<object?> { "CMD" };
                args.AddRange(cmd);
                test = args;
            }
        }

        if (test is null)
        {
            var httpGet = GetMap(probe, "httpGet");
            if (httpGet is not null)
            {
                var path = GetString(httpGet, "path") ?? "/";
                var portStr = GetInt(httpGet, "port")?.ToString() ?? GetString(httpGet, "port") ?? "80";
                var url = $"http://localhost:{portStr}{path}";
                test = isWindows
                    ? new List<object?> { "CMD", "powershell", "-command",
                        $"try {{ Invoke-WebRequest -UseBasicParsing -Uri '{url}' | Out-Null }} catch {{ exit 1 }}" }
                    // bash /dev/tcp is available in any bash-equipped image without requiring curl/wget.
                    : new List<object?> { "CMD-SHELL", $"bash -c 'echo > /dev/tcp/localhost/{portStr}'" };
            }
        }

        if (test is null)
        {
            var tcpSocket = GetMap(probe, "tcpSocket");
            if (tcpSocket is not null && !isWindows)
            {
                var portStr = GetInt(tcpSocket, "port")?.ToString() ?? GetString(tcpSocket, "port") ?? "80";
                test = new List<object?> { "CMD-SHELL", $"nc -z localhost {portStr}" };
            }
        }

        if (test is null) return null;

        var healthcheck = new Dictionary<string, object?> { ["test"] = test };

        var interval = GetInt(probe, "periodSeconds");
        if (interval.HasValue) healthcheck["interval"] = $"{interval.Value}s";

        var timeout = GetInt(probe, "timeoutSeconds");
        if (timeout.HasValue) healthcheck["timeout"] = $"{timeout.Value}s";

        var startPeriod = GetInt(probe, "initialDelaySeconds");
        if (startPeriod.HasValue) healthcheck["start_period"] = $"{startPeriod.Value}s";

        var retries = GetInt(probe, "failureThreshold");
        if (retries.HasValue) healthcheck["retries"] = retries.Value;

        return healthcheck;
    }

    private static Dictionary<string, object?>? GetMap(Dictionary<string, object?> map, params string[] path)
    {
        object? current = map;
        foreach (var segment in path)
        {
            if (current is Dictionary<string, object?> m && m.TryGetValue(segment, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }
        return current as Dictionary<string, object?>;
    }

    private static List<object?>? GetSequence(Dictionary<string, object?> map, params string[] path)
    {
        var node = GetNode(map, path);
        return node as List<object?>;
    }

    private static object? GetNode(Dictionary<string, object?> map, params string[] path)
    {
        object? current = map;
        foreach (var segment in path)
        {
            if (current is Dictionary<string, object?> m && m.TryGetValue(segment, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    private static string? GetString(Dictionary<string, object?> map, params string[] path)
    {
        var node = GetNode(map, path);
        return node as string;
    }

    private static int? GetInt(Dictionary<string, object?> map, params string[] path)
    {
        var node = GetNode(map, path);
        if (node is int i) return i;
        if (node is long l) return (int)l;
        if (node is string s && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    private static bool? GetBool(Dictionary<string, object?> map, params string[] path)
    {
        var node = GetNode(map, path);
        if (node is bool b) return b;
        if (node is string s && bool.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    private sealed record ConvertedRecord(string Name, string Kind, string Workload, string Os, string ComposeFile, string ServiceName);
    private sealed record UnconvertedRecord(string Name, string Kind, string Reason);

    private sealed class ComposeService
    {
        public string Name { get; set; } = string.Empty;
        public string Workload { get; set; } = string.Empty;
        public string Os { get; set; } = "linux";
        public string Image { get; set; } = string.Empty;
        public List<string> Ports { get; set; } = new();
        public Dictionary<string, string> Environment { get; set; } = new();
        public List<string> Volumes { get; set; } = new();
        public List<string> ExtraHosts { get; set; } = new();
        public HashSet<string> DependsOn { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Restart { get; set; } = "unless-stopped";
        public bool IsJob { get; set; }
        /// <summary>True for K8s init containers converted to run-once compose services.</summary>
        public bool IsInitContainer { get; set; }
        /// <summary>Named compose volumes (from emptyDir) required by this service.</summary>
        public HashSet<string> NamedVolumeRefs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string>? Command { get; set; }
        public List<string>? Args { get; set; }
        public Dictionary<string, object?>? Healthcheck { get; set; }
    }

    private List<string> BuildExtraHosts(RuleRuntimeContext runtimeContext, bool targetIsWindows)
    {
        if (!targetIsWindows)
        {
            return new List<string>();
        }

        return new List<string>();
    }


    private sealed class RuleRuntimeContext
    {
        private static readonly Regex CurlyTokenRegex = new(@"{{INFRA\[(?<infra>[^\]]+)\]\.(?:(?<scope>ENVIRONMENT)\[(?<key>[^\]]+)\]|(?<special>HOSTNAME))}}", RegexOptions.IgnoreCase);

        private readonly ILogger _logger;
        private readonly Dictionary<string, InfraServiceContext> _infra;
        private readonly Dictionary<string, SecretEntry> _secrets;
        // listen port -> connect (high) port
        private readonly Dictionary<int, int> _portProxyPorts;
        private readonly HashSet<string> _windowsWorkloads;
        private readonly List<string> _localServicePatterns;
        private readonly List<(string From, string To)> _urlOverrides;
        private readonly Func<int> _infraPortAllocator;

        private RuleRuntimeContext(ILogger logger, Func<int> infraPortAllocator)
        {
            _logger = logger;
            _infra = new Dictionary<string, InfraServiceContext>(StringComparer.OrdinalIgnoreCase);
            _secrets = new Dictionary<string, SecretEntry>(StringComparer.OrdinalIgnoreCase);
            _portProxyPorts = new Dictionary<int, int>();
            _windowsWorkloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _localServicePatterns = new List<string>();
            _urlOverrides = new List<(string, string)>();
            _infraPortAllocator = infraPortAllocator;
        }

        public bool IsWindowsWorkload(string workload) =>
            _windowsWorkloads.Any(pattern => workload.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<string> LocalServicePatterns => _localServicePatterns;
        public IReadOnlyList<(string From, string To)> UrlOverrides => _urlOverrides;
        public HashSet<string> InfraWithHealthcheck =>
            _infra.Values.Where(i => i.Healthcheck is not null).Select(i => i.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static RuleRuntimeContext Build(IReadOnlyList<DekomposeRuleSet> ruleSets, ILogger logger, Func<int> infraPortAllocator)
        {
            var context = new RuleRuntimeContext(logger, infraPortAllocator);
            if (ruleSets is null) return context;

            foreach (var rule in ruleSets)
            {
                logger.LogInformation("Applying rule {Rule} with {InfraCount} infra entries and {SecretScopeCount} secret scopes.",
                    rule.Match,
                    rule.Infrastructure?.Count ?? 0,
                    rule.SecretKeyRefs?.Count ?? 0);
                context.AddInfrastructure(rule.Infrastructure);
                context.AddSecrets(rule.SecretKeyRefs);
                if (rule.WindowsWorkloads is { Count: > 0 })
                {
                    foreach (var w in rule.WindowsWorkloads)
                        context._windowsWorkloads.Add(w);
                }
                if (rule.LocalServiceMatch is { Count: > 0 })
                {
                    foreach (var p in rule.LocalServiceMatch)
                        if (!string.IsNullOrWhiteSpace(p))
                            context._localServicePatterns.Add(p.Trim());
                }
                if (rule.UrlOverrides is { Count: > 0 })
                {
                    foreach (var o in rule.UrlOverrides)
                        if (!string.IsNullOrWhiteSpace(o.From))
                            context._urlOverrides.Add((o.From, o.To ?? string.Empty));
                }
            }

            logger.LogInformation("Initialized rule runtime context with {InfraCount} infra definitions and {SecretCount} secrets.",
                context._infra.Count,
                context._secrets.Count);
            return context;
        }

        private void AddInfrastructure(IEnumerable<DekomposeInfraDefinition>? definitions)
        {
            if (definitions is null) return;
            foreach (var def in definitions)
            {
                if (string.IsNullOrWhiteSpace(def.Name))
                {
                    continue;
                }
                var hasImage = !string.IsNullOrWhiteSpace(def.Image);
                var hasBuild = def.Build is not null && def.Build.Count > 0;
                if (!hasImage && !hasBuild)
                {
                    continue;
                }
                _infra[def.Name] = new InfraServiceContext(def, this, _infraPortAllocator);
            }
        }

        private void AddSecrets(Dictionary<string, List<DekomposeSecretDefinition>>? scopes)
        {
            if (scopes is null) return;
            foreach (var scope in scopes.Values)
            {
                if (scope is null) continue;
                foreach (var secret in scope)
                {
                    if (string.IsNullOrWhiteSpace(secret.Name)) continue;
                    var entry = BuildSecretEntry(secret);
                    if (entry is not null)
                    {
                        _secrets[secret.Name] = entry;
                    }
                }
            }
        }

        private SecretEntry? BuildSecretEntry(DekomposeSecretDefinition secret)
        {
            if (string.Equals(secret.Type, "file", StringComparison.OrdinalIgnoreCase))
            {
                if (!secret.Options.TryGetValue("filename", out var fileName) || string.IsNullOrWhiteSpace(fileName))
                {
                    _logger.LogWarning("File secret '{SecretName}' is missing a filename option.", secret.Name);
                    return null;
                }

                secret.Options.TryGetValue("value", out var rawValue);
                var useKubernetesLayout = true;
                if (secret.Options.TryGetValue("kubernetes-layout", out var layoutValue) &&
                    bool.TryParse(layoutValue, out var parsedLayout))
                {
                    useKubernetesLayout = parsedLayout;
                }

                var convertFromBase64 = true;
                if (secret.Options.TryGetValue("convert_from_base64", out var base64Flag) &&
                    bool.TryParse(base64Flag, out var parsedBase64))
                {
                    convertFromBase64 = parsedBase64;
                }

                return new SecretEntry
                {
                    File = new FileSecretEntry(
                        fileName.Trim(),
                        rawValue,
                        convertFromBase64,
                        useKubernetesLayout)
                };
            }

            if (string.Equals(secret.Type, "literal", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(secret.Type))
            {
                if (secret.Options.TryGetValue("value", out var literal))
                {
                    return new SecretEntry { Literal = literal };
                }

                _logger.LogWarning("Literal secret '{SecretName}' is missing a value option.", secret.Name);
                return null;
            }

            _logger.LogWarning("Unsupported secret type '{Type}' for '{SecretName}'.", secret.Type, secret.Name);
            return null;
        }

        public (string? Value, IReadOnlyCollection<string> InfraDependencies) ResolveSecretValue(string? secretName, string? key, string? targetOs)
        {
            SecretEntry? entry = null;
            string? resolvedName = null;

            if (!string.IsNullOrWhiteSpace(key) && _secrets.TryGetValue(key, out entry))
            {
                resolvedName = key;
            }
            else if (!string.IsNullOrWhiteSpace(secretName) && _secrets.TryGetValue(secretName, out entry))
            {
                resolvedName = secretName;
            }

            if (entry is null)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _logger.LogWarning("No secret value configured for key '{SecretKey}'.", key);
                }
                else if (!string.IsNullOrWhiteSpace(secretName))
                {
                    _logger.LogWarning("No secret value configured for secret '{SecretName}'.", secretName);
                }
                return (null, Array.Empty<string>());
            }

            var dependencies = entry.Literal is not null
                ? GetReferencedInfraNames(entry.Literal)
                : Array.Empty<string>();

            if (entry.Literal is not null)
            {
                return (Detokenize(entry.Literal, targetOs), dependencies);
            }

            if (entry.File is not null)
            {
                _logger.LogWarning("Secret '{SecretName}' is file-based and cannot be used as a literal value.", resolvedName);
            }

            return (null, dependencies);
        }

        public SecretFileMaterialization? ResolveSecretFile(string? secretName, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(secretName))
            {
                _logger.LogWarning("Volume references a secret without a name.");
                return null;
            }

            if (!_secrets.TryGetValue(secretName, out var entry) || entry.File is null)
            {
                _logger.LogWarning("No file secret configured for '{SecretName}'.", secretName);
                return null;
            }

            var fileEntry = entry.File;
            return MaterializeFileSecretInternal(secretName, fileEntry, outputDirectory, fileEntry.UseKubernetesLayout);
        }

        private SecretFileMaterialization? MaterializeFileSecretInternal(
            string secretName,
            FileSecretEntry fileEntry,
            string outputDirectory,
            bool kubernetesLayout)
        {
            if (string.IsNullOrWhiteSpace(fileEntry.RawValue))
            {
                _logger.LogWarning("File secret '{SecretName}' has no value to materialize.", secretName);
                return null;
            }

            try
            {
                var relativeDirectory = BuildSecretRelativeDirectory(secretName);
                var dataRelativePath = ResolveDataRelativePath(fileEntry, kubernetesLayout);
                var publishRelativePath = ResolvePublishRelativePath(fileEntry, kubernetesLayout);

                var dataFullPath = CombineAndEnsureSecretPath(outputDirectory, relativeDirectory, dataRelativePath);
                if (!File.Exists(dataFullPath))
                {
                    var dataBytes = ResolveSecretBytes(fileEntry, secretName);
                    if (dataBytes is null)
                    {
                        return null;
                    }
                    File.WriteAllBytes(dataFullPath, dataBytes);
                }

                if (!string.IsNullOrWhiteSpace(publishRelativePath) &&
                    !string.Equals(publishRelativePath, dataRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    var publishFullPath = CombineAndEnsureSecretPath(outputDirectory, relativeDirectory, publishRelativePath);
                    if (!File.Exists(publishFullPath))
                    {
                        File.Copy(dataFullPath, publishFullPath, overwrite: false);
                    }
                }

                fileEntry.MaterializedFullPath = dataFullPath;
                fileEntry.MaterializedRelativeDirectory = relativeDirectory;
                fileEntry.MaterializedRelativeDataFilePath = dataRelativePath;
                var published = string.IsNullOrWhiteSpace(publishRelativePath) ? dataRelativePath : publishRelativePath;

                return new SecretFileMaterialization(published, relativeDirectory, dataRelativePath, dataFullPath);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "File secret '{SecretName}' contains invalid base64 data.", secretName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to materialize file secret '{SecretName}'.", secretName);
            }

            return null;
        }

        private byte[]? ResolveSecretBytes(FileSecretEntry entry, string secretName)
        {
            try
            {
                if (entry.IsBase64)
                {
                    return Convert.FromBase64String(entry.RawValue ?? string.Empty);
                }

                return Encoding.UTF8.GetBytes(entry.RawValue ?? string.Empty);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "File secret '{SecretName}' contains invalid base64 data.", secretName);
                return null;
            }
        }

        private static string ResolveDataRelativePath(FileSecretEntry entry, bool kubernetesLayout)
        {
            if (kubernetesLayout)
            {
                return NormalizeRelativePath(Path.Combine("..data", entry.FileName));
            }

            return NormalizeRelativePath(entry.FileName);
        }

        private static string ResolvePublishRelativePath(FileSecretEntry entry, bool kubernetesLayout)
        {
            return NormalizeRelativePath(entry.FileName);
        }

        private static string CombineAndEnsureSecretPath(string outputDirectory, string relativeDirectory, string relativePath)
        {
            var combined = Path.Combine(
                outputDirectory,
                relativeDirectory,
                NormalizeRelativePath(relativePath));
            var parent = Path.GetDirectoryName(combined);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            return combined;
        }

        private static string NormalizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            var segments = path
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment == ".." ? throw new InvalidOperationException("Relative paths cannot traverse outside the secret directory.") : segment)
                .ToArray();

            if (segments.Length == 0) return string.Empty;
            return Path.Combine(segments);
        }


        public string Detokenize(string value, string? targetOs = null)
        {
            if (string.IsNullOrEmpty(value)) return value;

            return CurlyTokenRegex.Replace(value, match => ReplaceToken(match, targetOs));
        }

        public IReadOnlyCollection<string> GetReferencedInfraNames(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in CurlyTokenRegex.Matches(value))
            {
                var infraName = match.Groups["infra"].Value;
                if (string.IsNullOrWhiteSpace(infraName)) continue;
                if (_infra.TryGetValue(infraName, out var context))
                {
                    referenced.Add(context.Name);
                }
            }
            if (referenced.Count == _infra.Count) return referenced;

            foreach (var context in _infra.Values)
            {
                if (referenced.Contains(context.Name)) continue;
                if (InfraNameAppearsInValue(value, context.Name))
                {
                    referenced.Add(context.Name);
                    if (referenced.Count == _infra.Count)
                    {
                        break;
                    }
                }
            }

            return referenced;
        }

        public IReadOnlyCollection<string> GetInfraNamesForOs(bool isWindows)
        {
            return _infra.Values
                .Where(context => context.IsWindows == isWindows)
                .Select(context => context.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public bool IsInfraCompatible(string infraName, bool serviceIsWindows)
        {
            if (string.IsNullOrWhiteSpace(infraName)) return false;
            if (!_infra.TryGetValue(infraName, out var context)) return false;
            return context.IsWindows == serviceIsWindows;
        }

        private static bool InfraNameAppearsInValue(string value, string infraName)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(infraName)) return false;
            var pattern = $@"(?<![A-Za-z0-9_.-]){Regex.Escape(infraName)}(?![A-Za-z0-9_.-])";
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
        }

        private string ReplaceToken(Match match, string? targetOs)
        {
            var infraName = match.Groups["infra"].Value;
            if (!_infra.TryGetValue(infraName, out var context))
            {
                _logger.LogWarning("Token references unknown infra '{Infra}'.", infraName);
                return match.Value;
            }

            if (match.Groups["scope"].Success)
            {
                var key = match.Groups["key"].Value;
                if (!context.Environment.TryGetValue(key, out var replacement))
                {
                    _logger.LogWarning("Token references unknown environment '{Key}' on infra '{Infra}'.", key, infraName);
                    return match.Value;
                }

                return replacement;
            }

            if (match.Groups["special"].Success)
            {
                return ResolveHostToken(context, targetOs) ?? match.Value;
            }

            return match.Value;
        }

        private string? ResolveHostToken(InfraServiceContext context, string? targetOs)
        {
            if (string.Equals(targetOs, "windows", StringComparison.OrdinalIgnoreCase) && !context.IsWindows)
            {
                return NatGatewayPlaceholder;
            }
            return context.Name;
        }

        public async Task EmitInfraAsync(string outputDirectory, string networkName, ISerializer serializer, CancellationToken cancellationToken)
        {
            foreach (var infra in _infra.Values)
            {
                await infra.EmitAsync(outputDirectory, networkName, serializer, cancellationToken);
            }
        }

        public void RegisterPortProxyPorts(IEnumerable<string>? ports)
        {
            if (ports is null) return;

            foreach (var portDef in ports)
            {
                // portDef is "highPort:containerPort" — listenPort=containerPort, connectPort=highPort
                if (TryParseContainerPort(portDef, out var containerPort, out var hostPort)
                    && containerPort > 0 && hostPort > 0)
                {
                    _portProxyPorts[containerPort] = hostPort;
                }
            }
        }

        // Returns listenPort -> connectPort mapping
        public IReadOnlyDictionary<int, int> GetPortProxyPorts() => _portProxyPorts;

        public IList<object> GetInfraSummaries()
        {
            var list = new List<object>();
            foreach (var infra in _infra.Values)
            {
                list.Add(new
                {
                    infra.Name,
                    infra.Image,
                    infra.ComposeFile
                });
            }
            return list;
        }

        private sealed class InfraServiceContext
        {
            private readonly RuleRuntimeContext _runtimeContext;

            public InfraServiceContext(DekomposeInfraDefinition definition, RuleRuntimeContext runtimeContext, Func<int> infraPortAllocator)
            {
                _runtimeContext = runtimeContext;
                Name = definition.Name;
                Image = definition.Image;
                Command = definition.Command;
                Environment = new Dictionary<string, string>(definition.Environment ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                // Remap well-known container ports to random high host ports to avoid WSL2 port reservation conflicts.
                // "1433:1433" becomes e.g. "43210:1433". portproxy then listens on NAT:1433 -> 127.0.0.1:43210.
                Ports = RemapPorts(definition.Ports, infraPortAllocator);
                _runtimeContext.RegisterPortProxyPorts(Ports);
                Volumes = new List<string>(definition.Volumes ?? new List<string>());
                Healthcheck = definition.Healthcheck is null || definition.Healthcheck.Count == 0
                    ? null
                    : new Dictionary<string, object?>(definition.Healthcheck, StringComparer.OrdinalIgnoreCase);
                Build = definition.Build is null || definition.Build.Count == 0
                    ? null
                    : new Dictionary<string, object?>(definition.Build, StringComparer.OrdinalIgnoreCase);
                IsWindows = string.Equals(definition.Os, "windows", StringComparison.OrdinalIgnoreCase);
                OsSegment = string.IsNullOrWhiteSpace(definition.Os)
                    ? "infra"
                    : SanitizeSegment(definition.Os, "infra").ToLowerInvariant();
                ComposeFile = string.IsNullOrWhiteSpace(definition.ComposeFile)
                    ? $"docker-compose.{SanitizeSegment(Name, "infra")}.{OsSegment}.yml"
                    : definition.ComposeFile;

            }

            public string Name { get; }
            public string Image { get; }
            public Dictionary<string, object?>? Build { get; }
            public string? Command { get; }
            public Dictionary<string, string> Environment { get; }
            public List<string> Ports { get; }
            public List<string> Volumes { get; }
            public Dictionary<string, object?>? Healthcheck { get; }
            public string ComposeFile { get; }
            private string OsSegment { get; }
            public bool IsWindows { get; }
            private string TargetOs => IsWindows ? "windows" : "linux";

            private Dictionary<string, string> DetokenizeEnvironment()
            {
                var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in Environment)
                {
                    var detokenized = _runtimeContext.Detokenize(kvp.Value, TargetOs);
                    resolved[kvp.Key] = detokenized;
                }
                return resolved;
            }

            /// <summary>
            /// Rewrites each port definition so the host side uses a random high port.
            /// "1433:1433" → "43210:1433". Already-high host ports (>=40000) are left as-is.
            /// </summary>
            private static List<string> RemapPorts(IEnumerable<string> ports, Func<int> allocator)
            {
                var result = new List<string>();
                foreach (var p in ports)
                {
                    if (TryParseContainerPort(p, out var containerPort, out var hostPort) && hostPort < 40000)
                    {
                        result.Add($"{allocator()}:{containerPort}");
                    }
                    else
                    {
                        result.Add(p);
                    }
                }
                return result;
            }

            public async Task EmitAsync(string outputDirectory, string networkName, ISerializer serializer, CancellationToken cancellationToken)
            {
                if (string.IsNullOrWhiteSpace(Image) && (Build is null || Build.Count == 0)) return;

                var service = new Dictionary<string, object?>
                {
                    ["networks"] = new[] { networkName },
                    ["restart"] = "on-failure"
                };
                if (!string.IsNullOrWhiteSpace(Image))
                {
                    service["image"] = Image;
                }
                if (Build is not null && Build.Count > 0)
                {
                    service["build"] = Build;
                }

                if (!string.IsNullOrWhiteSpace(Command)) service["command"] = Command;
                if (Environment.Count > 0)
                {
                    var resolved = DetokenizeEnvironment();
                    if (resolved.Count > 0)
                    {
                        service["environment"] = resolved;
                    }
                }
                if (Ports.Count > 0) service["ports"] = Ports;
                if (Volumes.Count > 0) service["volumes"] = Volumes;
                if (Healthcheck is not null && Healthcheck.Count > 0)
                {
                    service["healthcheck"] = Healthcheck;
                }

                object? infraNetworkDef = IsWindows
                    ? new Dictionary<string, object?> { ["external"] = true, ["name"] = "nat" }
                    : (object?)new { };

                var compose = new
                {
                    services = new Dictionary<string, object?>
                    {
                        [Name] = service
                    },
                    networks = new Dictionary<string, object?>
                    {
                        [networkName] = infraNetworkDef
                    }
                };

                var path = Path.Combine(outputDirectory, ComposeFile);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, serializer.Serialize(compose), cancellationToken);
            }

        }

        private sealed class SecretEntry
        {
            public string? Literal { get; init; }
            public FileSecretEntry? File { get; init; }
        }

        private sealed class FileSecretEntry
        {
            public FileSecretEntry(string fileName, string? rawValue, bool isBase64, bool useKubernetesLayout)
            {
                FileName = fileName;
                RawValue = rawValue;
                IsBase64 = isBase64;
                UseKubernetesLayout = useKubernetesLayout;
            }

            public string FileName { get; }
            public string? RawValue { get; }
            public bool IsBase64 { get; }
            public bool UseKubernetesLayout { get; }
            public string? MaterializedFullPath { get; set; }
            public string? MaterializedRelativeDirectory { get; set; }
            public string? MaterializedRelativeDataFilePath { get; set; }
        }

        public sealed record SecretFileMaterialization(string RelativeFilePath, string RelativeDirectory, string? RelativeDataFilePath, string FullPath);

        private static bool IsWindowsOs(string? os) =>
            string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase);

        private static bool TryParseHostPort(string? definition, out int hostPort)
        {
            hostPort = 0;
            if (string.IsNullOrWhiteSpace(definition)) return false;

            var parts = definition.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            string candidate;
            if (parts.Length == 1)
            {
                candidate = parts[0];
            }
            else
            {
                candidate = parts[^2];
            }

            return int.TryParse(candidate, out hostPort);
        }

        /// <summary>
        /// Parses "hostPort:containerPort" into both parts.
        /// For single-token definitions containerPort == hostPort.
        /// </summary>
        private static bool TryParseContainerPort(string? definition, out int containerPort, out int hostPort)
        {
            containerPort = 0;
            hostPort = 0;
            if (string.IsNullOrWhiteSpace(definition)) return false;

            var parts = definition.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            if (parts.Length == 1)
            {
                if (!int.TryParse(parts[0], out var port)) return false;
                containerPort = port;
                hostPort = port;
                return true;
            }

            // last token is containerPort, second-to-last is hostPort
            if (!int.TryParse(parts[^1], out containerPort)) return false;
            if (!int.TryParse(parts[^2], out hostPort)) return false;
            return true;
        }
    }
}
