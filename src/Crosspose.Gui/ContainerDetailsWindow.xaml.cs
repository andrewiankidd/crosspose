using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging.Internal;
using Microsoft.Extensions.Logging;

namespace Crosspose.Gui;

public partial class ContainerDetailsWindow : Window
{
    private readonly ContainerRow _row;
    private readonly ILogger _logger;
    private readonly ProcessRunner _runner;
    private readonly InMemoryLogStore _logStore;
    private CancellationTokenSource? _cts;

    public ContainerDetailsWindow(ContainerRow row, ILoggerFactory loggerFactory, InMemoryLogStore logStore)
    {
        InitializeComponent();
        _row = row;
        _logger = loggerFactory.CreateLogger("crosspose.gui.containerdetails");
        _logStore = logStore;
        _runner = new ProcessRunner(loggerFactory.CreateLogger<ProcessRunner>())
        {
            OutputHandler = line => _logStore.Write(line)
        };
        DataContext = row;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshLogsAsync();
    }

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
            var (command, args) = GetLogsCommand();
            if (string.IsNullOrEmpty(command))
            {
                LogsStatus.Text = "Unknown platform for logs.";
                LogsText.Text = "Unable to determine platform to fetch logs.";
                return;
            }

            _logger.LogInformation("Fetching logs via {Command} {Args}", command, args);
            var result = await _runner.RunAsync(command, args, cancellationToken: token);
            LogsText.Text = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? "(no logs)"
                : result.StandardOutput;

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                LogsText.Text += Environment.NewLine + "---- stderr ----" + Environment.NewLine + result.StandardError;
            }

            LogsStatus.Text = result.IsSuccess ? "Logs loaded." : $"Failed to load logs (exit {result.ExitCode}).";
        }
        catch (OperationCanceledException)
        {
            LogsStatus.Text = "Log fetch canceled.";
        }
        catch (Exception ex)
        {
            LogsStatus.Text = "Failed to load logs.";
            LogsText.Text = ex.Message;
            _logger.LogError(ex, "Failed to fetch logs for {Id}", _row.UniqueId);
        }
        finally
        {
            LogsOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private (string command, string args) GetLogsCommand()
    {
        var unique = _row.UniqueId ?? string.Empty;
        if (unique.StartsWith("docker:", StringComparison.OrdinalIgnoreCase))
        {
            return ("docker", $"logs --tail 500 {unique["docker:".Length..]}");
        }

        if (unique.StartsWith("podman:", StringComparison.OrdinalIgnoreCase))
        {
            return ("podman", $"logs --tail 500 {unique["podman:".Length..]}");
        }

        if (unique.StartsWith("wsl-podman:", StringComparison.OrdinalIgnoreCase))
        {
            var distro = CrossposeEnvironment.WslDistro;
            var id = unique["wsl-podman:".Length..];
            return ("wsl", $"--distribution {distro} --exec podman logs --tail 500 {id}");
        }

        return (string.Empty, string.Empty);
    }
}
