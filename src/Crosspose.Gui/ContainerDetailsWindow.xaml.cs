using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Crosspose.Core.Logging.Internal;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Gui;

public partial class ContainerDetailsWindow : Window
{
    private readonly ContainerRow _row;
    private readonly ILogger _logger;
    private readonly IContainerPlatformRunner _runner;
    private CancellationTokenSource? _cts;

    // Lazy-load flags — each tab fetches once, then only on explicit Refresh
    private bool _inspectLoaded;
    private bool _statsLoaded;

    public ContainerDetailsWindow(ContainerRow row, ILoggerFactory loggerFactory, InMemoryLogStore logStore, IContainerPlatformRunner runner)
    {
        InitializeComponent();
        _row = row;
        _logger = loggerFactory.CreateLogger("crosspose.gui.containerdetails");
        _runner = runner;
        DataContext = row;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _cts?.Cancel();
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
            var result = await _runner.GetContainerLogsAsync(id, tail: 500, token);
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

    // ── View models ────────────────────────────────────────────────────────────

    public record KeyValueItem(string Key, string Value);

    public record MountItem(string Type, string Source, string Destination, string Mode, bool Rw)
    {
        public string RwLabel => Rw ? "rw" : "ro";
    }
}
