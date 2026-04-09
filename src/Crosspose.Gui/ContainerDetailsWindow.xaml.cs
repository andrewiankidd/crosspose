using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Crosspose.Core.Deployment;
using Crosspose.Core.Logging.Internal;
using Crosspose.Core.Orchestration;
using Crosspose.Doctor.Core;
using Microsoft.Extensions.Logging;

namespace Crosspose.Gui;

public partial class ContainerDetailsWindow : Window
{
    private readonly ContainerRow _row;
    private readonly ILogger _logger;
    private readonly IContainerPlatformRunner _runner;
    private readonly ComposeOrchestrator _orchestrator;
    private readonly OciRegistryStore _ociStore;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _tagCts;

    // Lazy-load flags — each tab fetches once, then only on explicit Refresh
    private bool _inspectLoaded;
    private bool _statsLoaded;

    public ContainerDetailsWindow(ContainerRow row, ILoggerFactory loggerFactory, InMemoryLogStore logStore, IContainerPlatformRunner runner, ComposeOrchestrator orchestrator)
    {
        InitializeComponent();
        _row = row;
        _logger = loggerFactory.CreateLogger("crosspose.gui.containerdetails");
        _runner = runner;
        _orchestrator = orchestrator;
        _ociStore = new OciRegistryStore(_logger);
        DataContext = row;
        Title = $"Container Details: {row.Id}";
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _cts?.Cancel();
            _tagCts?.Cancel();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await RefreshLogsAsync();

    // ── Tab routing ────────────────────────────────────────────────────────────

    private async void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != DetailTabControl) return;
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not TabItem tab) return;

        switch (tab.Header?.ToString())
        {
            case "Inspect" when !_inspectLoaded:
                await RefreshInspectAsync();
                _inspectLoaded = true;
                break;
            case "Bind mounts" when !_inspectLoaded:
                await RefreshInspectAsync();
                _inspectLoaded = true;
                break;
            case "Stats" when !_statsLoaded:
                await RefreshStatsAsync();
                _statsLoaded = true;
                break;
        }
    }

    // ── Logs ───────────────────────────────────────────────────────────────────

    private async void OnRefreshLogs(object sender, RoutedEventArgs e) => await RefreshLogsAsync();

    private async Task RefreshLogsAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            LogsOverlay.Visibility = Visibility.Visible;
            LogsStatus.Text = "Loading logs...";
            var id = _row.UniqueId ?? string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                LogsStatus.Text = "Unknown platform.";
                LogsText.Text = "Unable to determine platform to fetch logs.";
                return;
            }

            _logger.LogInformation("Fetching logs for {Id}", id);
            var result = await _runner.GetContainerLogsAsync(id, tail: 500, timestamps: true, token);
            LogsText.Text = string.IsNullOrWhiteSpace(result.StandardOutput) ? "(no logs)" : result.StandardOutput;
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                LogsText.Text += Environment.NewLine + "---- stderr ----" + Environment.NewLine + result.StandardError;

            LogsStatus.Text = result.IsSuccess ? "Logs loaded." : $"Failed (exit {result.ExitCode}).";
        }
        catch (OperationCanceledException) { LogsStatus.Text = "Canceled."; }
        catch (Exception ex)
        {
            LogsStatus.Text = "Failed.";
            LogsText.Text = ex.Message;
            _logger.LogError(ex, "Failed to fetch logs for {Id}", _row.UniqueId);
        }
        finally { LogsOverlay.Visibility = Visibility.Collapsed; }
    }

    // ── Inspect ────────────────────────────────────────────────────────────────

    private async void OnRefreshInspect(object sender, RoutedEventArgs e)
    {
        _inspectLoaded = false;
        await RefreshInspectAsync();
        _inspectLoaded = true;
    }

    private async Task RefreshInspectAsync()
    {
        var id = _row.UniqueId ?? string.Empty;
        if (string.IsNullOrEmpty(id))
        {
            InspectStatus.Text = "Unknown platform.";
            return;
        }

        InspectLoadingOverlay.Visibility = Visibility.Visible;
        MountsLoadingOverlay.Visibility = Visibility.Visible;
        InspectStatus.Text = "Loading...";
        MountsStatus.Text = "Loading...";

        try
        {
            var data = await _runner.InspectContainerAsync(id);
            if (data is null)
            {
                InspectStatus.Text = "Inspect failed.";
                MountsStatus.Text = InspectStatus.Text;
                return;
            }

            PopulateInspect(data);
        }
        catch (Exception ex)
        {
            InspectStatus.Text = "Parse error.";
            MountsStatus.Text = "Parse error.";
            _logger.LogError(ex, "Failed to inspect container {Id}", _row.UniqueId);
        }
        finally
        {
            InspectLoadingOverlay.Visibility = Visibility.Collapsed;
            MountsLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulateInspect(ContainerInspectResult data)
    {
        var overview = new List<KeyValueItem> { new("ID", data.Id), new("Created", data.Created) };
        if (!string.IsNullOrWhiteSpace(data.WorkingDir)) overview.Add(new("Working dir", data.WorkingDir));
        overview.Add(new("User", string.IsNullOrWhiteSpace(data.User) ? "(default)" : data.User));
        if (data.Cmd is not null) overview.Add(new("Cmd", data.Cmd));
        if (data.Entrypoint is not null) overview.Add(new("Entrypoint", data.Entrypoint));
        if (data.RestartPolicy is not null) overview.Add(new("Restart policy", data.RestartPolicy));
        if (data.NetworkMode is not null) overview.Add(new("Network mode", data.NetworkMode));
        if (data.MemoryLimit is not null) overview.Add(new("Memory limit", data.MemoryLimit));
        foreach (var net in data.Networks) overview.Add(new(net.Key, net.Value));

        InspectOverviewList.ItemsSource = overview;
        EnvVarsList.ItemsSource = data.EnvVars.Select(kv => new KeyValueItem(kv.Key, kv.Value)).ToList();
        InspectPortsList.ItemsSource = data.Ports.Select(kv => new KeyValueItem(kv.Key, kv.Value)).ToList();
        LabelsList.ItemsSource = data.Labels.Select(kv => new KeyValueItem(kv.Key, kv.Value)).ToList();
        InspectStatus.Text = $"Loaded — {overview.Count} properties, {data.EnvVars.Count} env vars, {data.Labels.Count} labels.";

        MountsList.ItemsSource = data.Mounts.Select(m => new MountItem(m.Type, m.Source, m.Destination, m.Mode, m.Rw)).ToList();
        MountsStatus.Text = $"{data.Mounts.Count} mount(s).";
    }

    // ── Exec ───────────────────────────────────────────────────────────────────

    private void OnExecCommandKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnExecRun(sender, e);
    }

    private async void OnExecRun(object sender, RoutedEventArgs e)
    {
        var userCmd = ExecCommandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userCmd)) return;

        var id = _row.UniqueId ?? string.Empty;
        if (string.IsNullOrEmpty(id))
        {
            ExecStatus.Text = "Unknown platform.";
            return;
        }

        ExecStatus.Text = "Running...";
        try
        {
            ExecOutput.AppendText($"> {userCmd}{Environment.NewLine}");
            var result = await _runner.ExecInContainerAsync(id, userCmd);
            if (!string.IsNullOrEmpty(result.StandardOutput))
                ExecOutput.AppendText(result.StandardOutput);
            if (!string.IsNullOrEmpty(result.StandardError))
                ExecOutput.AppendText("--- stderr ---" + Environment.NewLine + result.StandardError);
            ExecOutput.AppendText(Environment.NewLine);
            ExecStatus.Text = result.IsSuccess ? $"Exited {result.ExitCode}." : $"Failed (exit {result.ExitCode}).";
            ExecOutput.ScrollToEnd();
        }
        catch (Exception ex)
        {
            ExecOutput.AppendText(ex.Message + Environment.NewLine);
            ExecStatus.Text = "Error.";
        }
    }

    private void OnExecClear(object sender, RoutedEventArgs e)
    {
        ExecOutput.Clear();
        ExecStatus.Text = string.Empty;
    }

    // ── Stats ──────────────────────────────────────────────────────────────────

    private async void OnRefreshStats(object sender, RoutedEventArgs e)
    {
        _statsLoaded = false;
        await RefreshStatsAsync();
        _statsLoaded = true;
    }

    private async Task RefreshStatsAsync()
    {
        StatsLoadingOverlay.Visibility = Visibility.Visible;
        StatsStatus.Text = "Loading...";
        try
        {
            var id = _row.UniqueId ?? string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                StatsStatus.Text = "Unknown platform.";
                return;
            }

            var stats = await _runner.GetContainerStatsAsync(id);
            var items = new List<KeyValueItem>();
            if (stats is not null)
            {
                if (stats.CpuPercent is not null)    items.Add(new("CPU %",        stats.CpuPercent));
                if (stats.MemoryUsage is not null)   items.Add(new("Memory usage", stats.MemoryUsage));
                if (stats.MemoryPercent is not null) items.Add(new("Memory %",     stats.MemoryPercent));
                if (stats.MemoryLimit is not null)   items.Add(new("Memory limit", stats.MemoryLimit));
                if (stats.NetIO is not null)         items.Add(new("Net I/O",      stats.NetIO));
                if (stats.BlockIO is not null)       items.Add(new("Block I/O",    stats.BlockIO));
                if (stats.Pids is not null)          items.Add(new("PIDs",         stats.Pids));
            }

            StatsList.ItemsSource = items;
            StatsStatus.Text = stats is not null ? "Loaded." : "No stats available.";
        }
        catch (Exception ex)
        {
            StatsStatus.Text = "Failed.";
            _logger.LogError(ex, "Failed to fetch stats for {Id}", _row.UniqueId);
        }
        finally { StatsLoadingOverlay.Visibility = Visibility.Collapsed; }
    }

    // ── Image tag edit ─────────────────────────────────────────────────────────

    private void OnEditImageStart(object sender, RoutedEventArgs e)
    {
        var (_, _, currentTag) = ParseImageRef(_row.Image);
        ImageTagCombo.Text = currentTag;
        ImageTagCombo.ItemsSource = null;
        TagFetchStatus.Text = "Loading tags...";
        SaveTagBtn.IsEnabled = false;
        ImageViewPanel.Visibility = Visibility.Collapsed;
        ImageEditPanel.Visibility = Visibility.Visible;
        ImageTagCombo.Focus();
        _tagCts?.Cancel();
        _tagCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = LoadTagsAsync(_tagCts.Token);
    }

    private async Task LoadTagsAsync(CancellationToken ct)
    {
        var (registry, repo, _) = ParseImageRef(_row.Image);
        List<string> tags;
        try
        {
            if (DoctorSettings.IsOfflineMode)
                tags = await FetchLocalTagsAsync(registry, repo, ct);
            else
            {
                tags = (await _ociStore.ListTagsForRepositoryAsync(registry, repo, ct)).ToList();
                if (tags.Count == 0)
                    tags = await FetchLocalTagsAsync(registry, repo, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tag fetch failed for {Image}", _row.Image);
            tags = new List<string>();
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (ImageEditPanel.Visibility != Visibility.Visible) return;
            ImageTagCombo.ItemsSource = tags;
            TagFetchStatus.Text = tags.Count > 0 ? $"{tags.Count} tag(s)" : "(no tags found)";
            SaveTagBtn.IsEnabled = true;
        });
    }

    private async Task<List<string>> FetchLocalTagsAsync(string registry, string repo, CancellationToken ct)
    {
        var imageRef = string.IsNullOrWhiteSpace(registry) ? repo : $"{registry}/{repo}";
        var images = await _runner.GetImagesDetailedAsync(ct);
        return images
            .Where(img => img.Name.Equals(imageRef, StringComparison.OrdinalIgnoreCase))
            .Select(img => img.Tag)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    [SupportedOSPlatform("windows")]
    private async void OnImageTagSave(object sender, RoutedEventArgs e)
    {
        var newTag = ImageTagCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(newTag)) return;

        var (registry, repo, oldTag) = ParseImageRef(_row.Image);
        if (newTag.Equals(oldTag, StringComparison.Ordinal)) { ExitEditMode(); return; }

        var newImage = string.IsNullOrWhiteSpace(registry)
            ? $"{repo}:{newTag}"
            : $"{registry}/{repo}:{newTag}";

        // _row.Project is the Docker/Podman compose project label = the deployment dir name
        // (e.g. "helm-platform" or "helm-platform-1" for collision) under deployBase.
        var projectDir = DeploymentMetadataStore.FindDeploymentDirectory(_row.Project);

        if (projectDir is null)
        {
            TagFetchStatus.Text = "Deployment not found — update compose file manually";
            return;
        }

        // Update image reference in every compose file that references it
        var updated = false;
        foreach (var file in Directory.GetFiles(projectDir, "docker-compose.*.yml"))
        {
            var yaml = File.ReadAllText(file);
            if (!yaml.Contains(_row.Image)) continue;
            File.WriteAllText(file, yaml.Replace(_row.Image, newImage));
            updated = true;
        }

        if (!updated)
        {
            TagFetchStatus.Text = "Image not found in compose files";
            return;
        }

        SaveTagBtn.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            // If we have stored credentials for this registry, log both runtimes in
            // before the pull so podman in WSL can authenticate. Failure is non-fatal —
            // the pull error will surface below if it still can't authenticate.
            if (!string.IsNullOrWhiteSpace(registry))
            {
                var entry = _ociStore.TryGetEntryForHost(registry);
                if (entry?.Username is not null && entry?.Password is not null)
                {
                    TagFetchStatus.Text = $"Logging in to {registry}...";
                    _logger.LogInformation("Running registry login for {Registry} before image pull", registry);
                    await _runner.LoginAsync(registry, entry.Username, entry.Password, cts.Token);
                }
            }

            // Remove just this container by ID (rm -f stops it if running, then removes).
            // Other containers are completely untouched.
            TagFetchStatus.Text = "Removing old container...";
            await _runner.RemoveContainerAsync(_row.UniqueId, cts.Token);

            // compose up -d sees no container for this service and creates a fresh one
            // with the updated image. Services that still have containers are left alone.
            TagFetchStatus.Text = "Applying...";
            var request = new ComposeExecutionRequest(
                SourcePath: projectDir,
                Action: ComposeAction.Up,
                Detached: true,
                ProjectName: _row.Project);
            var result = await _orchestrator.ExecuteAsync(request, cts.Token);

            // Surface any compose error. podman-compose writes pull errors to stdout,
            // not stderr, so fall back to stdout when stderr is empty.
            var composeError = ExtractComposeError(result.PodmanResult)
                ?? ExtractComposeError(result.DockerResult);

            if (composeError is not null)
            {
                _logger.LogError("Compose up failed after image tag change: {Error}", composeError);

                var isAuthError =
                    composeError.Contains("authentication required", StringComparison.OrdinalIgnoreCase) ||
                    composeError.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                    composeError.Contains("invalid username/password", StringComparison.OrdinalIgnoreCase);

                TagFetchStatus.Text = isAuthError
                    ? $"Auth failed for {(string.IsNullOrWhiteSpace(registry) ? "registry" : registry)} — check Doctor or add credentials in Sources"
                    : FirstMeaningfulLine(composeError, maxLength: 90);

                SaveTagBtn.IsEnabled = true;
                return;
            }

            _row.Image = newImage;
            ImageViewLabel.Text = newImage;
            ExitEditMode();

            // Refresh logs so old entries from the previous container are replaced
            await RefreshLogsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply image tag change for {Container}", _row.Id);
            TagFetchStatus.Text = $"Failed: {ex.Message[..Math.Min(ex.Message.Length, 60)]}";
            SaveTagBtn.IsEnabled = true;
        }
    }

    private void OnImageTagCancel(object sender, RoutedEventArgs e) => ExitEditMode();

    private void ExitEditMode()
    {
        _tagCts?.Cancel();
        ImageEditPanel.Visibility = Visibility.Collapsed;
        ImageViewPanel.Visibility = Visibility.Visible;
    }

    private static string? ExtractComposeError(PlatformCommandResult? r)
    {
        if (r?.HasError != true) return null;
        var text = !string.IsNullOrWhiteSpace(r.Error) ? r.Error : r.Result.StandardOutput;
        return string.IsNullOrWhiteSpace(text) ? $"Exit code {r.Result.ExitCode}" : text.Trim();
    }

    // Strip ANSI escape codes and return the first non-trivial line, capped at maxLength.
    private static string FirstMeaningfulLine(string text, int maxLength)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-9;]*[mK]", "");
        var line = clean.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 4) ?? clean.Trim();
        return line.Length <= maxLength ? line : line[..maxLength] + "…";
    }

    private static (string registry, string repo, string tag) ParseImageRef(string image)
    {
        var tag = string.Empty;
        // Split off tag at last colon, but only if no slash after the colon
        var lastColon = image.LastIndexOf(':');
        if (lastColon > 0 && !image[lastColon..].Contains('/'))
        {
            tag = image[(lastColon + 1)..];
            image = image[..lastColon];
        }

        var firstSlash = image.IndexOf('/');
        if (firstSlash > 0)
        {
            var candidate = image[..firstSlash];
            if (candidate.Contains('.') || candidate.Contains(':') ||
                candidate.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return (candidate, image[(firstSlash + 1)..], tag);
            }
        }

        return (string.Empty, image, tag);
    }

    // ── View models ────────────────────────────────────────────────────────────

    public record KeyValueItem(string Key, string Value);

    public record MountItem(string Type, string Source, string Destination, string Mode, bool Rw)
    {
        public string RwLabel => Rw ? "rw" : "ro";
    }
}
