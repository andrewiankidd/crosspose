using System.Text.Json;

namespace Crosspose.Core.Orchestration;

public record ContainerInspectResult(
    string Id,
    string Created,
    string? WorkingDir,
    string? User,
    string? Cmd,
    string? Entrypoint,
    string? RestartPolicy,
    string? NetworkMode,
    string? MemoryLimit,
    IReadOnlyList<KeyValuePair<string, string>> Networks,
    IReadOnlyList<KeyValuePair<string, string>> EnvVars,
    IReadOnlyList<KeyValuePair<string, string>> Ports,
    IReadOnlyList<KeyValuePair<string, string>> Labels,
    IReadOnlyList<ContainerMount> Mounts
);

public record ContainerMount(string Type, string Source, string Destination, string Mode, bool Rw)
{
    public string RwLabel => Rw ? "rw" : "ro";
}

public record ContainerStatsResult(
    string? CpuPercent,
    string? MemoryUsage,
    string? MemoryPercent,
    string? MemoryLimit,
    string? NetIO,
    string? BlockIO,
    string? Pids
);

public static class ContainerInspectParser
{
    public static ContainerInspectResult? ParseInspect(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement[0]
            : doc.RootElement;

        var config = root.TryGetProperty("Config", out var cfg) ? cfg : (JsonElement?)null;
        var hostConfig = root.TryGetProperty("HostConfig", out var hc) ? hc : (JsonElement?)null;

        var fullId = GetStr(root, "Id") ?? GetStr(root, "ID") ?? "";
        if (fullId.Length > 12) fullId = fullId[..12];

        string? workingDir = null, user = null, cmd = null, entrypoint = null;
        if (config.HasValue)
        {
            workingDir = GetStr(config.Value, "WorkingDir");
            user = GetStr(config.Value, "User");
            if (config.Value.TryGetProperty("Cmd", out var cmdEl))
                cmd = JsonArrayOrString(cmdEl);
            if (config.Value.TryGetProperty("Entrypoint", out var epEl) && epEl.ValueKind != JsonValueKind.Null)
                entrypoint = JsonArrayOrString(epEl);
        }

        string? restartPolicy = null, networkMode = null, memoryLimit = null;
        if (hostConfig.HasValue)
        {
            if (hostConfig.Value.TryGetProperty("RestartPolicy", out var rp))
            {
                var name = GetStr(rp, "Name") ?? "";
                var max = rp.TryGetProperty("MaximumRetryCount", out var mc) && mc.TryGetInt32(out var mci) && mci > 0
                    ? $" (max {mci})" : "";
                restartPolicy = name + max;
            }
            networkMode = GetStr(hostConfig.Value, "NetworkMode");
            if (hostConfig.Value.TryGetProperty("Memory", out var mem) && mem.TryGetInt64(out var memBytes) && memBytes > 0)
                memoryLimit = FormatBytes(memBytes);
        }

        var networks = new List<KeyValuePair<string, string>>();
        if (root.TryGetProperty("NetworkSettings", out var ns) && ns.TryGetProperty("Networks", out var nets)
            && nets.ValueKind == JsonValueKind.Object)
        {
            foreach (var net in nets.EnumerateObject())
            {
                var ip = GetStr(net.Value, "IPAddress");
                if (!string.IsNullOrWhiteSpace(ip))
                    networks.Add(new($"IP ({net.Name})", ip));
            }
        }

        var envVars = new List<KeyValuePair<string, string>>();
        if (config.HasValue && config.Value.TryGetProperty("Env", out var envArr)
            && envArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var env in envArr.EnumerateArray())
            {
                var s = env.GetString() ?? "";
                var idx = s.IndexOf('=');
                envVars.Add(idx >= 0 ? new(s[..idx], s[(idx + 1)..]) : new(s, ""));
            }
        }

        var ports = new List<KeyValuePair<string, string>>();
        if (root.TryGetProperty("NetworkSettings", out var ns2) && ns2.TryGetProperty("Ports", out var portsObj)
            && portsObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var port in portsObj.EnumerateObject())
            {
                if (port.Value.ValueKind == JsonValueKind.Null || port.Value.GetArrayLength() == 0)
                {
                    ports.Add(new(port.Name, "(not published)"));
                    continue;
                }
                var bindings = new List<string>();
                foreach (var b in port.Value.EnumerateArray())
                {
                    var hip = GetStr(b, "HostIp") ?? "0.0.0.0";
                    var hp = GetStr(b, "HostPort") ?? "";
                    bindings.Add(string.IsNullOrEmpty(hip) || hip == "0.0.0.0" ? hp : $"{hip}:{hp}");
                }
                ports.Add(new(port.Name, string.Join(", ", bindings)));
            }
        }

        var labels = new List<KeyValuePair<string, string>>();
        if (config.HasValue && config.Value.TryGetProperty("Labels", out var labelsObj)
            && labelsObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var lbl in labelsObj.EnumerateObject())
                labels.Add(new(lbl.Name, lbl.Value.GetString() ?? ""));
        }

        var mounts = new List<ContainerMount>();
        if (root.TryGetProperty("Mounts", out var mountsArr) && mountsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in mountsArr.EnumerateArray())
            {
                var type = GetStr(m, "Type") ?? "bind";
                var src = GetStr(m, "Source") ?? GetStr(m, "source") ?? "";
                var dst = GetStr(m, "Destination") ?? GetStr(m, "destination") ?? "";
                var mode = GetStr(m, "Mode") ?? GetStr(m, "mode") ?? "";
                var rw = m.TryGetProperty("RW", out var rwProp) && rwProp.ValueKind == JsonValueKind.True;
                mounts.Add(new(type, src, dst, mode, rw));
            }
        }

        return new ContainerInspectResult(
            Id: fullId,
            Created: GetStr(root, "Created") ?? "",
            WorkingDir: workingDir,
            User: user,
            Cmd: cmd,
            Entrypoint: entrypoint,
            RestartPolicy: restartPolicy,
            NetworkMode: networkMode,
            MemoryLimit: memoryLimit,
            Networks: networks,
            EnvVars: envVars,
            Ports: ports,
            Labels: labels,
            Mounts: mounts
        );
    }

    public static ContainerStatsResult? ParseStats(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.TrimStart().StartsWith("{"));
        if (line is null) return null;

        using var doc = JsonDocument.Parse(line.Trim());
        var r = doc.RootElement;

        return new ContainerStatsResult(
            CpuPercent:    GetStatStr(r, "CPUPerc", "cpu"),
            MemoryUsage:   GetStatStr(r, "MemUsage", "mem_usage"),
            MemoryPercent: GetStatStr(r, "MemPerc", "mem_perc"),
            MemoryLimit:   GetStatStr(r, "MemLimit", "mem_limit"),
            NetIO:         GetNetIO(r),
            BlockIO:       GetBlockIO(r),
            Pids:          GetStatStr(r, "PIDs", "pids")
        );
    }

    private static string? GetStatStr(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (root.TryGetProperty(key, out var val))
            {
                var s = val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    private static string? GetNetIO(JsonElement root)
    {
        if (root.TryGetProperty("NetIO", out var v)) return v.GetString() ?? v.ToString();
        var input = GetStatStr(root, "net_input");
        var output = GetStatStr(root, "net_output");
        if (input is not null && output is not null) return $"{input} / {output}";
        return input ?? output;
    }

    private static string? GetBlockIO(JsonElement root)
    {
        if (root.TryGetProperty("BlockIO", out var v)) return v.GetString() ?? v.ToString();
        var input = GetStatStr(root, "block_input");
        var output = GetStatStr(root, "block_output");
        if (input is not null && output is not null) return $"{input} / {output}";
        return input ?? output;
    }

    private static string? GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string JsonArrayOrString(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
            return string.Join(" ", el.EnumerateArray().Select(e => e.GetString() ?? ""));
        if (el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? "";
        return el.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / 1024} KB";
    }
}
