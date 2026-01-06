using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging.Internal;
using Crosspose.Core.Orchestration;
using Crosspose.Core.Deployment;
using Microsoft.Extensions.Logging;

namespace Crosspose.Gui;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DockerContainerRunner _dockerRunner;
    private readonly PodmanContainerRunner _podmanRunner;
    private readonly CombinedContainerPlatformRunner _combinedRunner;
    private readonly ComposeOrchestrator _composeOrchestrator;
    private readonly InMemoryLogStore _logStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly DefinitionDeploymentService _deploymentService;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isRefreshing;
    private bool _pendingRefresh;
    private Task _refreshTask = Task.CompletedTask;
    private ProjectEntry? _selectedProjectEntry;
    private DeploymentRow? _selectedDeploymentRow;
    public ObservableCollection<ProjectGroupRow> ContainerGroups { get; } = new();
    public ObservableCollection<ProjectEntry> Projects { get; } = new();
    public ObservableCollection<ImageRow> Images { get; } = new();
    public ObservableCollection<VolumeRow> Volumes { get; } = new();
    public ObservableCollection<DeploymentRow> Deployments { get; } = new();
    public ObservableCollection<string> Errors { get; } = new();
    public string LogOutput => _logStore.ReadAll();
    private string _infoText = string.Empty;
    public string InfoText
    {
        get => _infoText;
        set
        {
            if (_infoText != value)
            {
                _infoText = value;
                OnPropertyChanged(nameof(InfoText));
            }
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        _logStore = new InMemoryLogStore();
        _logStore.OnWrite += _ => OnPropertyChanged(nameof(LogOutput));
        _loggerFactory = Crosspose.Core.Logging.CrossposeLoggerFactory.Create(LogLevel.Information, _logStore);
        _logger = _loggerFactory.CreateLogger("crosspose.gui");

        var processRunner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>())
        {
            OutputHandler = line => _logStore.Write(line)
        };
        _dockerRunner = new DockerContainerRunner(processRunner);
        _podmanRunner = new PodmanContainerRunner(processRunner, runInsideWsl: true, wslDistribution: CrossposeEnvironment.WslDistro);
        _combinedRunner = new CombinedContainerPlatformRunner(_dockerRunner, _podmanRunner);
        _composeOrchestrator = new ComposeOrchestrator(_dockerRunner, _podmanRunner, _loggerFactory);
        _deploymentService = new DefinitionDeploymentService();

        var intervalSeconds = GetRefreshIntervalSeconds();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(intervalSeconds)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshCurrentViewAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SidebarRuntime.SelectedIndex = 0; // Projects default
        await RefreshCurrentViewAsync();
        _refreshTimer.Start();
        OnPropertyChanged(nameof(LogOutput));
        InfoText = "View and manage cross-platform compose definitions that are ready for use.";
    }

    private async Task ShowContainersAsync(bool force = false)
    {
        // ensure UI switches immediately even if a refresh is already running
        HideAllViews();
        ViewTitle.Text = "Containers";
        ContainersHeader.Visibility = Visibility.Visible;
        ContainersTree.Visibility = Visibility.Visible;
        ContainersToolbar.Visibility = Visibility.Visible;

        if (_isRefreshing)
        {
            if (force)
            {
                _pendingRefresh = true;
            }
            return;
        }

        _isRefreshing = true;
        _refreshTask = RefreshContainersInternal();
        try
        {
            await _refreshTask.ConfigureAwait(false);
        }
        finally
        {
            _isRefreshing = false;
            if (_pendingRefresh)
            {
                _pendingRefresh = false;
                _ = ShowContainersAsync(); // fire and forget; no await to avoid recursion deadlock
            }
        }
    }

    private async Task RefreshContainersInternal()
    {
        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _logger.LogInformation("Refreshing containers view...");
        InfoText = "View and manage combined containers from Docker and Podman.";
        var newErrors = new List<string>();
        var previouslySelectedContainers = GetSelectedContainerIds();

        var detailTask = _combinedRunner.GetContainersGroupedByProjectAsync(includeAll: true, cancellationToken: cts.Token);
        var rawTask = _combinedRunner.GetContainersAsync(includeAll: true, cancellationToken: cts.Token);

        try
        {
            await Task.WhenAll(detailTask, rawTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var message = "Container refresh timed out after 15s.";
            newErrors.Add(message);
            _logger.LogWarning(message);
            return;
        }

        var rawContainers = rawTask.Result;
        if (rawContainers.HasError)
        {
            var containerError = FormatPlatformError("container refresh", rawContainers);
            newErrors.Add(containerError);
            _logger.LogWarning("Container enumeration error: {Error}", containerError);
        }

        var groups = detailTask.Result;
        _logger.LogInformation("Loaded {Count} containers in {Elapsed}ms", groups.Sum(g => g.Containers.Count), sw.ElapsedMilliseconds);

        var groupedRows = new Dictionary<string, List<ContainerRow>>(StringComparer.OrdinalIgnoreCase);
        var newContainers = new List<ContainerRow>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ContainerRow BuildRow(ContainerProcessInfo container, string projectKey)
        {
            var hostPlatform = string.IsNullOrWhiteSpace(container.HostPlatform)
                ? (container.Platform.Contains("podman", StringComparison.OrdinalIgnoreCase) ? "lin" : "win")
                : container.HostPlatform;
            hostPlatform = hostPlatform.Trim().ToLowerInvariant();

            return new ContainerRow
            {
                UniqueId = $"{container.Platform}:{container.Id}",
                Platform = container.Platform,
                HostPlatform = hostPlatform,
                Id = container.Name,
                Image = container.Image,
                Ports = container.Ports,
                State = container.State,
                Status = container.Status,
                Project = projectKey,
                IsRunning = container.IsRunning,
                ExitState = DetermineExitState(container),
                IsSelected = previouslySelectedContainers.Contains($"{container.Platform}:{container.Id}")
            };
        }

        void AddRow(ContainerRow row)
        {
            newContainers.Add(row);
            if (!groupedRows.TryGetValue(row.Project, out var list))
            {
                list = new List<ContainerRow>();
                groupedRows[row.Project] = list;
            }
            list.Add(row);
            seenIds.Add(row.UniqueId);
            seenNames.Add(row.Id);
        }

        foreach (var group in groups)
        {
            var projectKey = string.IsNullOrWhiteSpace(group.Project) ? string.Empty : group.Project;
            foreach (var container in group.Containers)
            {
                var row = BuildRow(container, projectKey);
                AddRow(row);
            }
        }

        foreach (var extra in ParseDockerTable(rawContainers.Result.StandardOutput))
        {
            var uid = $"docker:{extra.Id}";
            if (seenIds.Contains(uid) || seenNames.Contains(extra.Name))
            {
                continue;
            }

            var projKey = extra.Project ?? string.Empty;
            var row = new ContainerRow
            {
                UniqueId = uid,
                Platform = "docker",
                HostPlatform = "win",
                Id = extra.Name,
                Image = extra.Image,
                Ports = extra.Ports,
                State = extra.State,
                Status = extra.Status,
                Project = projKey,
                IsRunning = extra.IsRunning,
                ExitState = DetermineExitState(extra),
                IsSelected = previouslySelectedContainers.Contains(uid)
            };

            AddRow(row);
        }

        var grouped = groupedRows
            .OrderBy(kvp => string.IsNullOrWhiteSpace(kvp.Key) ? 1 : 0)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new GroupedEntries(kvp.Key, kvp.Value));
        await Dispatcher.InvokeAsync(() =>
        {
            var expansion = ContainerGroups.ToDictionary(p => p.Name, p => p.IsExpanded, StringComparer.OrdinalIgnoreCase);
            ContainerGroups.Clear();
            foreach (var group in grouped)
            {
                var pg = new ProjectGroupRow
                {
                    Name = string.IsNullOrWhiteSpace(group.Key) ? "(unassigned)" : group.Key,
                    IsExpanded = expansion.TryGetValue(string.IsNullOrWhiteSpace(group.Key) ? "(unassigned)" : group.Key, out var ex)
                        ? ex
                        : true
                };
                foreach (var row in group.Rows) pg.Containers.Add(row);
                ContainerGroups.Add(pg);
            }

            Errors.Clear();
            foreach (var err in newErrors) Errors.Add(err);
            OnPropertyChanged(nameof(LogOutput));
            UpdateContainerButtons();
        });

        _isRefreshing = false;
    }

    private async Task RefreshCurrentViewAsync(bool force = false)
    {
        try
        {
            var selection = GetCurrentView();
            switch (selection)
            {
                case "Definitions":
                    await ShowProjectsAsync();
                    break;
                case "Projects":
                    await ShowDeploymentsAsync();
                    break;
                case "Containers":
                    await ShowContainersAsync(force);
                    break;
                case "Images":
                    await ShowImagesAsync(force);
                    break;
                case "Volumes":
                    await ShowVolumesAsync(force);
                    break;
                default:
                    await ShowContainersAsync(force);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh view {View}", GetCurrentView());
            Errors.Clear();
            Errors.Add(ex.Message);
        }
    }

    private async Task ShowImagesAsync(bool force = false)
    {
        if (_isRefreshing && !force) return;
        _isRefreshing = true;
        HideAllViews();
        ViewTitle.Text = "Images";
        InfoText = "View and manage combined container images from Docker and Podman.";
        ImagesList.Visibility = Visibility.Visible;
        ImagesToolbar.Visibility = Visibility.Visible;

        _logger.LogInformation("Refreshing images view...");
        var newErrors = new List<string>();
        var newImages = new List<ImageRow>();
        var previouslySelectedImages = GetSelectedImageKeys();

        var detailTask = _combinedRunner.GetImagesDetailedAsync();
        var rawTask = _combinedRunner.GetImagesAsync();
        await Task.WhenAll(detailTask, rawTask);

        if (rawTask.Result.HasError)
        {
            newErrors.Add(FormatPlatformError("image refresh", rawTask.Result));
        }

        foreach (var img in detailTask.Result)
        {
            var host = NormalizePlatformIcon(img.HostPlatform, img.Platform);
            var row = new ImageRow
            {
                HostPlatform = host,
                Platform = img.Platform,
                Name = img.Name,
                Tag = img.Tag,
                Id = img.Id,
                Size = img.Size,
                State = "available"
            };
            row.IsSelected = previouslySelectedImages.Contains(BuildImageKey(row));
            newImages.Add(row);
        }

        await Dispatcher.InvokeAsync(() =>
        {
            Images.Clear();
            foreach (var i in newImages) Images.Add(i);

            Errors.Clear();
            foreach (var err in newErrors) Errors.Add(err);
            UpdateImageButtons();
        });

        _isRefreshing = false;
    }

    private async Task ShowVolumesAsync(bool force = false)
    {
        if (_isRefreshing && !force) return;
        _isRefreshing = true;
        HideAllViews();
        ViewTitle.Text = "Volumes";
        InfoText = "View and manage combined container volumes from Docker and Podman..";
        VolumesList.Visibility = Visibility.Visible;
        VolumesToolbar.Visibility = Visibility.Visible;

        _logger.LogInformation("Refreshing volumes view...");
        var newErrors = new List<string>();
        var newVolumes = new List<VolumeRow>();
        var previouslySelectedVolumes = GetSelectedVolumeKeys();

        var detailTask = _combinedRunner.GetVolumesDetailedAsync();
        var rawTask = _combinedRunner.GetVolumesAsync();
        await Task.WhenAll(detailTask, rawTask);

        if (rawTask.Result.HasError)
        {
            newErrors.Add(FormatPlatformError("volume refresh", rawTask.Result));
        }

        foreach (var vol in detailTask.Result)
        {
            var host = NormalizePlatformIcon(vol.HostPlatform, vol.Platform);
            var row = new VolumeRow
            {
                HostPlatform = host,
                Platform = vol.Platform,
                Name = vol.Name,
                Size = vol.Size,
                State = "available"
            };
            row.IsSelected = previouslySelectedVolumes.Contains(BuildVolumeKey(row));
            newVolumes.Add(row);
        }

        await Dispatcher.InvokeAsync(() =>
        {
            Volumes.Clear();
            foreach (var v in newVolumes) Volumes.Add(v);

            Errors.Clear();
            foreach (var err in newErrors) Errors.Add(err);
            UpdateVolumeButtons();
        });

        _isRefreshing = false;
    }

    private async Task ShowDeploymentsAsync()
    {
        HideAllViews();
        ViewTitle.Text = "Projects";
        InfoText = "View and manage cross-platform compose definitions that are ready for use.";
        DeploymentsList.Visibility = Visibility.Visible;
        DeploymentsToolbar.Visibility = Visibility.Visible;

        var deploymentRoot = CrossposeEnvironment.DeploymentDirectory;
        Directory.CreateDirectory(deploymentRoot);
        var previouslySelectedPath = _selectedDeploymentRow?.FullPath;

        try
        {
            var rows = await Task.Run(() => EnumerateDeploymentRows(deploymentRoot)).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                Deployments.Clear();
                DeploymentRow? match = null;
                foreach (var row in rows)
                {
                    Deployments.Add(row);
                    if (match is null &&
                        previouslySelectedPath is not null &&
                        previouslySelectedPath.Equals(row.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        match = row;
                    }
                }

                if (match is not null)
                {
                    _selectedDeploymentRow = match;
                    DeploymentsList.SelectedItem = match;
                    DeploymentsList.UpdateLayout();
                    DeploymentsList.ScrollIntoView(match);
                }
                else
                {
                    _selectedDeploymentRow = null;
                    DeploymentsList.SelectedItem = null;
                }
                UpdateDeploymentButtons();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate deployments in {Directory}", deploymentRoot);
            Errors.Clear();
            Errors.Add($"Unable to enumerate deployments: {ex.Message}");
        }
    }

    private static List<DeploymentRow> EnumerateDeploymentRows(string deploymentRoot)
    {
        var rows = new List<DeploymentRow>();
        if (!Directory.Exists(deploymentRoot)) return rows;

        foreach (var projectDir in Directory.GetDirectories(deploymentRoot))
        {
            var projectName = Path.GetFileName(projectDir);
            var versionDirs = Directory.GetDirectories(projectDir);
            if (versionDirs.Length == 0)
            {
                var single = CreateDeploymentRow(projectDir, projectName, projectName);
                if (single is not null) rows.Add(single);
                continue;
            }

            foreach (var versionDir in versionDirs)
            {
                var versionName = Path.GetFileName(versionDir);
                var row = CreateDeploymentRow(versionDir, projectName, versionName);
                if (row is not null) rows.Add(row);
            }
        }

        return rows
            .OrderByDescending(r => DateTime.TryParse(r.LastUpdated, out var parsed) ? parsed : DateTime.MinValue)
            .ToList();
    }

    private static DeploymentRow? CreateDeploymentRow(string directory, string projectFallback, string versionFallback)
    {
        if (!Directory.Exists(directory)) return null;
        var metadata = DeploymentMetadataStore.Read(directory);
        if (metadata is null && !HasComposeFiles(directory))
        {
            return null;
        }

        var info = new DirectoryInfo(directory);
        var lastAction = metadata?.LastAction;
        if (string.IsNullOrWhiteSpace(lastAction))
        {
            lastAction = "None";
        }

        return new DeploymentRow
        {
            Project = metadata?.Project ?? projectFallback,
            Version = metadata?.Version ?? versionFallback,
            FullPath = directory,
            LastAction = lastAction,
            LastUpdated = info.LastWriteTime.ToString("g")
        };
    }

    private static bool HasComposeFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory, "docker-compose.*.yml", SearchOption.TopDirectoryOnly).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task ShowProjectsAsync()
    {
        HideAllViews();
        ViewTitle.Text = "Definitions";
        InfoText = "View and manage your saved cross-platform compose definitions.";
        ProjectsPanel.Visibility = Visibility.Visible;
        DefinitionsToolbar.Visibility = Visibility.Visible;
        _logger.LogInformation("Refreshing projects view...");

        // Preserve selection across refreshes.
        var selectedPath = _selectedProjectEntry?.FullPath;

        try
        {
            var basePath = CrossposeEnvironment.OutputDirectory;
            var path = Path.GetFullPath(basePath);
            var zips = await Task.Run(() =>
                Directory.Exists(path)
                    ? Directory.GetFiles(path, "*.zip", SearchOption.TopDirectoryOnly)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : Array.Empty<string>());

            Projects.Clear();

            foreach (var zip in zips)
            {
                var fileName = Path.GetFileNameWithoutExtension(zip);
                var (projectName, versionName) = SplitProjectAndVersion(fileName);
                var modified = File.GetLastWriteTime(zip);

                var entry = new ProjectEntry
                {
                    Name = projectName,
                    Version = versionName,
                    FullPath = zip,
                    LastWriteTime = modified,
                    FileCount = 1
                };
                Projects.Add(entry);
            }

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                var match = Projects.FirstOrDefault(p =>
                    p.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    _selectedProjectEntry = match;
                    ProjectsListView.SelectedItem = match;
                    ProjectsListView.UpdateLayout();
                    ProjectsListView.ScrollIntoView(match);
                }
                else
                {
                    _selectedProjectEntry = null;
                }
            }
            else
            {
                _selectedProjectEntry = null;
            }
            UpdateProjectButtons();
            _logger.LogInformation("Found {Count} compose definitions under {Path}", Projects.Count, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate compose project zip files.");
            Errors.Clear();
            Errors.Add(ex.Message);
            _logStore.Write($"Definitions error: {ex.Message}");
            OnPropertyChanged(nameof(LogOutput));
        }
    }

    private static string NormalizePlatformIcon(string hostPlatform, string platform)
    {
        var source = !string.IsNullOrWhiteSpace(hostPlatform) ? hostPlatform : platform;
        if (string.IsNullOrWhiteSpace(source)) return "lin";
        return source.IndexOf("win", StringComparison.OrdinalIgnoreCase) >= 0 ? "win" : "lin";
    }

    private static string FormatPlatformError(string context, PlatformCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return result.Error;
        }

        return $"The {context} request failed for {result.Platform} (exit {result.Result.ExitCode}). See Logs for details.";
    }

    private static List<string> CollectComposeFailures(ComposeExecutionResult result)
    {
        var failures = new List<string>();
        if (result.DockerResult?.HasError == true)
        {
            failures.Add(FormatComposeFailure(result.DockerResult));
        }
        if (result.PodmanResult?.HasError == true)
        {
            failures.Add(FormatComposeFailure(result.PodmanResult));
        }
        return failures;
    }

    private static string FormatComposeFailure(PlatformCommandResult commandResult)
    {
        var error = commandResult.Error;
        if (string.IsNullOrWhiteSpace(error))
        {
            error = commandResult.Result.StandardOutput;
        }
        if (string.IsNullOrWhiteSpace(error))
        {
            error = $"Exit code {commandResult.Result.ExitCode}.";
        }
        return $"{commandResult.Platform}: {error.Trim()}";
    }

    private void HideAllViews()
    {
        ContainersHeader.Visibility = Visibility.Collapsed;
        ContainersTree.Visibility = Visibility.Collapsed;
        ImagesList.Visibility = Visibility.Collapsed;
        VolumesList.Visibility = Visibility.Collapsed;
        ProjectsPanel.Visibility = Visibility.Collapsed;
        DeploymentsList.Visibility = Visibility.Collapsed;
        PlaceholderText.Visibility = Visibility.Collapsed;
        DefinitionsToolbar.Visibility = Visibility.Collapsed;
        DeploymentsToolbar.Visibility = Visibility.Collapsed;
        ContainersToolbar.Visibility = Visibility.Collapsed;
        ImagesToolbar.Visibility = Visibility.Collapsed;
        VolumesToolbar.Visibility = Visibility.Collapsed;
    }

    private List<ContainerRow> GetSelectedContainerRows() =>
        ContainerGroups.SelectMany(g => g.Containers).Where(c => c.IsSelected).ToList();

    private List<ImageRow> GetSelectedImageRows() =>
        Images.Where(i => i.IsSelected).ToList();

    private List<VolumeRow> GetSelectedVolumeRows() =>
        Volumes.Where(v => v.IsSelected).ToList();

    private HashSet<string> GetSelectedContainerIds()
    {
        return Dispatcher.Invoke(() =>
            ContainerGroups
                .SelectMany(g => g.Containers)
                .Where(c => c.IsSelected)
                .Select(c => c.UniqueId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private HashSet<string> GetSelectedImageKeys()
    {
        return Dispatcher.Invoke(() =>
            Images
                .Where(i => i.IsSelected)
                .Select(BuildImageKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private HashSet<string> GetSelectedVolumeKeys()
    {
        return Dispatcher.Invoke(() =>
            Volumes
                .Where(v => v.IsSelected)
                .Select(BuildVolumeKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildImageKey(ImageRow row) =>
        $"{row.Platform}|{row.Name}|{row.Tag}|{row.Id}";

    private static string BuildImageIdentifier(ImageRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Id)) return row.Id;
        if (!string.IsNullOrWhiteSpace(row.Name))
        {
            var tag = string.IsNullOrWhiteSpace(row.Tag) ? "latest" : row.Tag;
            return $"{row.Name}:{tag}";
        }
        return string.Empty;
    }

    private static string BuildVolumeKey(VolumeRow row) =>
        $"{row.Platform}|{row.Name}";

    private void UpdateContainerButtons()
    {
        var hasSelection = ContainerGroups.SelectMany(g => g.Containers).Any(c => c.IsSelected);
        ContainersStartButton.IsEnabled = hasSelection;
        ContainersStopButton.IsEnabled = hasSelection;
        ContainersDetailsButton.IsEnabled = hasSelection;
        ContainersDeleteButton.IsEnabled = hasSelection;
    }

    private void UpdateImageButtons()
    {
        var hasSelection = Images.Any(i => i.IsSelected);
        ImagesPullButton.IsEnabled = hasSelection;
        ImagesDeleteButton.IsEnabled = hasSelection;
    }

    private void UpdateVolumeButtons()
    {
        var hasSelection = Volumes.Any(v => v.IsSelected);
        VolumesDeleteButton.IsEnabled = hasSelection;
        VolumesPruneButton.IsEnabled = hasSelection;
        VolumesInspectButton.IsEnabled = hasSelection;
    }

    private string GetCurrentView()
    {
        if (SidebarSetup.SelectedItem is ListBoxItem si && si.IsSelected) return si.Content?.ToString() ?? string.Empty;
        if (SidebarRuntime.SelectedItem is ListBoxItem ri && ri.IsSelected) return ri.Content?.ToString() ?? string.Empty;
        return string.Empty;
    }

    private Task ShowPlaceholderAsync(string viewName)
    {
        HideAllViews();
        ViewTitle.Text = viewName;
        InfoText = viewName switch
        {
            "Definitions" => "Manage saved compose project outputs.",
            "Projects" => "Review and deploy compose outputs.",
            "Volumes" => "View and manage combined container volumes from Docker and Podman..",
            "Images" => "View and manage combined container images from Docker and Podman.",
            _ => $"{viewName} view not implemented yet."
        };
        PlaceholderText.Visibility = Visibility.Visible;
        PlaceholderText.Text = $"{viewName} view not implemented yet.";
        return Task.CompletedTask;
}
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static IReadOnlyList<ContainerProcessInfo> ParseDockerTable(string combinedOutput)
    {
        const string dockerHeader = "=== containers (docker) ===";
        const string podmanHeader = "=== containers (podman) ===";

        var dockerSectionStart = combinedOutput.IndexOf(dockerHeader, StringComparison.OrdinalIgnoreCase);
        if (dockerSectionStart < 0) return Array.Empty<ContainerProcessInfo>();

        dockerSectionStart += dockerHeader.Length;
        var dockerSectionEnd = combinedOutput.IndexOf(podmanHeader, dockerSectionStart, StringComparison.OrdinalIgnoreCase);
        if (dockerSectionEnd < 0) dockerSectionEnd = combinedOutput.Length;

        var dockerSection = combinedOutput[dockerSectionStart..dockerSectionEnd];
        var lines = dockerSection.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) return Array.Empty<ContainerProcessInfo>();

        var list = new List<ContainerProcessInfo>();
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = Regex.Split(lines[i].Trim(), "\\s{2,}").Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (parts.Length < 6) continue;

            var id = parts[0];
            var image = parts[1];
            var statusFull = parts[4];
            var state = statusFull.Split(' ')[0];
            var ports = parts.Length > 5 ? parts[5] : string.Empty;
            var name = parts[^1];

            list.Add(new ContainerProcessInfo(
                Platform: "docker",
                Id: id,
                Name: name,
                Image: image,
                Status: statusFull,
                State: state,
                Ports: ports,
                Project: null,
                HostPlatform: "win"));
        }

        return list;
    }

        private static ContainerExitState DetermineExitState(IContainerProcess info)
        {
            if (info is null) return ContainerExitState.Unknown;
            if (info.IsRunning) return ContainerExitState.Running;
            var state = info.State?.Trim().ToLowerInvariant() ?? string.Empty;
            if (state == "exited")
            {
                if (TryParseExitCode(info.Status, out var code))
                {
                    return code == 0 ? ContainerExitState.ExitedSuccess : ContainerExitState.ExitedFailure;
                }

                return ContainerExitState.ExitedUnknown;
            }

            return ContainerExitState.Unknown;
        }

        private static bool TryParseExitCode(string status, out int code)
        {
            code = 0;
            if (string.IsNullOrWhiteSpace(status)) return false;
            var match = Regex.Match(status, @"\((\-?\d+)\)");
            if (!match.Success) return false;
            return int.TryParse(match.Groups[1].Value, out code);
        }

    private async void OnSidebarSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender == SidebarSetup && SidebarSetup.SelectedItem != null)
        {
            SidebarRuntime.SelectionChanged -= OnSidebarSelectionChanged;
            SidebarRuntime.SelectedItem = null;
            SidebarRuntime.SelectionChanged += OnSidebarSelectionChanged;
        }
        else if (sender == SidebarRuntime && SidebarRuntime.SelectedItem != null)
        {
            SidebarSetup.SelectionChanged -= OnSidebarSelectionChanged;
            SidebarSetup.SelectedItem = null;
            SidebarSetup.SelectionChanged += OnSidebarSelectionChanged;
        }
        await RefreshCurrentViewAsync(true);
    }

    private void OnLogClearRequested(object sender, EventArgs e)
    {
        _logStore.Clear();
        OnPropertyChanged(nameof(LogOutput));
    }

    private void OnLearnMore(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Learn More link {Link}", e.Uri);
        }
        e.Handled = true;
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => Close();

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RefreshCurrentViewAsync(true);
    }

    private void OnProjectsNew(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Launching Crosspose Dekompose from Definitions view.");
        OnDekomposeClick(sender, e);
    }

    private void OnProjectsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedProjectEntry = ProjectsListView.SelectedItem as ProjectEntry;
        UpdateProjectButtons();
    }

    private void UpdateProjectButtons()
    {
        var hasSelection = _selectedProjectEntry != null;
        ProjectsDeleteProjectButton.IsEnabled = hasSelection;
        ProjectsDeployButton.IsEnabled = hasSelection;
    }

    private void UpdateDeploymentButtons()
    {
        var hasSelection = _selectedDeploymentRow != null;
        DeploymentsUpButton.IsEnabled = hasSelection;
        DeploymentsDownButton.IsEnabled = hasSelection;
        DeploymentsRestartButton.IsEnabled = hasSelection;
        DeploymentsStopButton.IsEnabled = hasSelection;
        DeploymentsStartButton.IsEnabled = hasSelection;
        DeploymentsRemoveButton.IsEnabled = hasSelection;
    }

    private void OnProjectsOpen(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = CrossposeEnvironment.OutputDirectory;
            Directory.CreateDirectory(folder);
            _logger.LogInformation("Opening definitions folder {Folder}", folder);
            Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open definitions folder {Folder}", CrossposeEnvironment.OutputDirectory);
            MessageBox.Show(this, "Unable to open folder.\n\n" + ex.Message, "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnProjectsDeploy(object sender, RoutedEventArgs e)
    {
        if (_selectedProjectEntry is null)
        {
            MessageBox.Show(this, "Select a compose definition first.", "Crosspose", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var entry = _selectedProjectEntry;
            var deploymentResult = await PrepareDeploymentAsync(entry);
            var deploymentPath = deploymentResult.TargetPath;
            _logger.LogInformation("Prepared deployment at {DeploymentPath} from {Source}", deploymentPath, entry.FullPath);

            var request = new ComposeExecutionRequest(
                deploymentPath,
                ComposeAction.Up,
                Detached: true,
                ProjectName: entry.Name);

            var result = await _composeOrchestrator.ExecuteAsync(request);
            AppendComposeLog(result);

            var failures = CollectComposeFailures(result);
            if (failures.Count > 0)
            {
                var separator = Environment.NewLine + Environment.NewLine;
                var message = $"Compose deployment completed with errors:{Environment.NewLine}{Environment.NewLine}{string.Join(separator, failures)}{Environment.NewLine}{Environment.NewLine}See Logs for additional details.";
                MessageBox.Show(this, message, "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(this, $"Deployment '{entry.Name}' prepared and launched successfully.", "Crosspose", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compose action failed for {Path}", _selectedProjectEntry.FullPath);
            MessageBox.Show(this, "Compose action failed.\n\n" + ex.Message, "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnProjectsDeleteProject(object sender, RoutedEventArgs e)
    {
        if (_selectedProjectEntry == null) return;
        try
        {
            var targetName = _selectedProjectEntry.Name;

            // Ensure any running deployments for this project are torn down first.
            var relatedDeployments = Deployments
                .Where(d => d.Project.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var deployment in relatedDeployments)
            {
                await ExecuteDeploymentActionAsync(deployment, ComposeAction.Stop);
                await ExecuteDeploymentActionAsync(deployment, ComposeAction.Down);
            }

            var toDelete = Projects.Where(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var entry in toDelete)
            {
                if (File.Exists(entry.FullPath))
                {
                    File.Delete(entry.FullPath);
                }
                Projects.Remove(entry);
            }

            foreach (var dep in relatedDeployments)
            {
                Deployments.Remove(dep);
            }

            _selectedProjectEntry = null;
            UpdateProjectButtons();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete project {Project}", _selectedProjectEntry?.FullPath);
            MessageBox.Show(this, "Failed to delete project: " + ex.Message, "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnContainersStart(object sender, RoutedEventArgs e)
    {
        var targets = GetSelectedContainerRows().Where(c => !c.IsRunning).ToList();
        foreach (var row in targets)
        {
            var ok = await _combinedRunner.StartContainerAsync(row.UniqueId);
            if (ok)
            {
                row.IsRunning = true;
            }
        }
    }

    private async void OnContainersStop(object sender, RoutedEventArgs e)
    {
        var targets = GetSelectedContainerRows().Where(c => c.IsRunning).ToList();
        foreach (var row in targets)
        {
            var ok = await _combinedRunner.StopContainerAsync(row.UniqueId);
            if (ok)
            {
                row.IsRunning = false;
            }
        }
    }

    private void OnContainerPortsClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not TextBlock textBlock) return;
        var portsDescription = textBlock.Text;
        if (string.IsNullOrWhiteSpace(portsDescription)) return;

        if (!TryExtractHostEndpoint(portsDescription, out var host, out var port))
        {
            _logger.LogDebug("Unable to parse exposed port from '{Ports}'", portsDescription);
            return;
        }

        var safeHost = NormalizeHostForLaunch(host);
        var target = $"http://{safeHost}:{port}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch browser for {Target}", target);
            MessageBox.Show(this, $"Unable to open {target}.{Environment.NewLine}{Environment.NewLine}{ex.Message}", "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool TryExtractHostEndpoint(string description, out string host, out int port)
    {
        host = "localhost";
        port = 0;
        if (string.IsNullOrWhiteSpace(description)) return false;

        var parts = description
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
        if (parts.Count == 0) return false;

        var token = parts.FirstOrDefault(part => part.Contains("->", StringComparison.Ordinal)) ?? parts.First();

        if (string.IsNullOrWhiteSpace(token)) return false;

        var arrowIndex = token.IndexOf("->", StringComparison.Ordinal);
        if (arrowIndex > 0)
        {
            token = token[..arrowIndex].Trim();
        }

        var protocolIndex = token.IndexOf('/', StringComparison.Ordinal);
        if (protocolIndex > 0)
        {
            token = token[..protocolIndex];
        }

        // IPv6 in [::1]:8080 form
        if (token.StartsWith("[", StringComparison.Ordinal))
        {
            var close = token.IndexOf("]:", StringComparison.Ordinal);
            if (close > 1 && close + 2 < token.Length)
            {
                var hostPart = token[1..close];
                var portPart = token[(close + 2)..];
                if (int.TryParse(portPart, out port))
                {
                    host = hostPart;
                    return true;
                }
            }

            return false;
        }

        var colonIndex = token.LastIndexOf(':');
        if (colonIndex < 0)
        {
            if (int.TryParse(token, out port))
            {
                host = "localhost";
                return true;
            }
            return false;
        }

        var left = token[..colonIndex];
        var right = token[(colonIndex + 1)..];

        if (string.Equals(left.Trim(), "*", StringComparison.Ordinal))
        {
            return false;
        }

        // Format hostIp:hostPort or hostname:hostPort (docker)
        if (IsLikelyHost(left) && int.TryParse(right, out port))
        {
            host = left;
            return true;
        }

        // Format hostPort:containerPort (podman formatter)
        if (int.TryParse(left, out port))
        {
            return true;
        }

        return false;
    }

    private static bool IsLikelyHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed == "*") return false;
        if (trimmed.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("::", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return trimmed.Any(ch => char.IsLetter(ch) || ch == '.' || ch == ':');
    }

    private static string NormalizeHostForLaunch(string host)
    {
        if (string.IsNullOrWhiteSpace(host) ||
            host == "*" ||
            host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::", StringComparison.OrdinalIgnoreCase))
        {
            return "localhost";
        }

        var trimmed = host.Trim();
        trimmed = trimmed.Trim('[', ']');
        return trimmed.Contains(':') ? $"[{trimmed}]" : trimmed;
    }

    private async void OnContainersDelete(object sender, RoutedEventArgs e)
    {
        var targets = GetSelectedContainerRows();
        if (targets.Count == 0) return;

        var failures = new List<string>();
        foreach (var row in targets)
        {
            try
            {
                var ok = await _combinedRunner.RemoveContainerAsync(row.UniqueId);
                if (!ok)
                {
                    failures.Add(row.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete container {Container}", row.Id);
                failures.Add($"{row.Id}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            MessageBox.Show(this, $"Failed to delete containers:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}", "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await RefreshCurrentViewAsync(true);
    }

    private void OnContainersDetails(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedContainerRows().FirstOrDefault();
        if (row is null) return;
        var details = new ContainerDetailsWindow(row, _loggerFactory, _logStore) { Owner = this };
        details.Show();
    }

    private void OnContainerSelectionChanged(object? sender, RoutedEventArgs e) => UpdateContainerButtons();
    private void OnContainersSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ContainersTree.SelectedItem is ContainerRow row)
        {
            if (!row.IsSelected)
            {
                row.IsSelected = true;
            }
            UpdateContainerButtons();
        }
    }
    private void OnImageSelectionChanged(object? sender, RoutedEventArgs e) => UpdateImageButtons();
    private void OnVolumeSelectionChanged(object? sender, RoutedEventArgs e) => UpdateVolumeButtons();

    private void OnImagesSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateImageButtons();

    private void OnVolumesSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateVolumeButtons();

    private async void OnImagesDelete(object sender, RoutedEventArgs e)
    {
        var targets = GetSelectedImageRows();
        if (targets.Count == 0) return;

        var failures = new List<string>();
        foreach (var row in targets)
        {
            var identifier = BuildImageIdentifier(row);
            if (string.IsNullOrWhiteSpace(identifier))
            {
                failures.Add($"{row.Name} (no identifier)");
                continue;
            }

            try
            {
                var ok = await _combinedRunner.RemoveImageAsync($"{row.Platform}:{identifier}");
                if (!ok)
                {
                    failures.Add($"{row.Name}:{row.Tag}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete image {Image}", row.Name);
                failures.Add($"{row.Name}:{row.Tag} -> {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            MessageBox.Show(this, $"Failed to delete images:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}", "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await RefreshCurrentViewAsync(true);
    }

    private async void OnVolumesDelete(object sender, RoutedEventArgs e)
    {
        var targets = GetSelectedVolumeRows();
        if (targets.Count == 0) return;

        var failures = new List<string>();
        foreach (var row in targets)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                failures.Add("(unnamed volume)");
                continue;
            }

            try
            {
                var ok = await _combinedRunner.RemoveVolumeAsync($"{row.Platform}:{row.Name}");
                if (!ok)
                {
                    failures.Add(row.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete volume {Volume}", row.Name);
                failures.Add($"{row.Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            MessageBox.Show(this, $"Failed to delete volumes:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}", "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await RefreshCurrentViewAsync(true);
    }

    private void OnImagesPull(object sender, RoutedEventArgs e) => NotifyNotImplemented("Image pull");
    private void OnVolumesPrune(object sender, RoutedEventArgs e) => NotifyNotImplemented("Volume prune");
    private void OnVolumesInspect(object sender, RoutedEventArgs e) => NotifyNotImplemented("Volume inspect");

    private void NotifyNotImplemented(string capability)
    {
        MessageBox.Show(this, $"{capability} is not available yet.", "Crosspose", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private Task<DefinitionDeploymentResult> PrepareDeploymentAsync(ProjectEntry entry)
    {
        var request = new DefinitionDeploymentRequest
        {
            BaseDirectory = CrossposeEnvironment.DeploymentDirectory,
            ProjectName = entry.Name,
            Version = entry.Version,
            SourcePath = entry.FullPath
        };
        return _deploymentService.PrepareAsync(request);
    }

    private async Task ExecuteDeploymentActionAsync(DeploymentRow row, ComposeAction action)
    {
        ComposeExecutionResult? executionResult = null;
        try
        {
            var request = new ComposeExecutionRequest(
                SourcePath: row.FullPath,
                Action: action,
                Detached: action == ComposeAction.Up,
                ProjectName: row.Project);

            executionResult = await _composeOrchestrator.ExecuteAsync(request);
            AppendComposeLog(executionResult);

            var label = $"{action.ToCommand()} @ {DateTime.Now:t}";
            row.LastAction = label;
            row.LastUpdated = DateTime.Now.ToString("g");
            DeploymentMetadataStore.Update(row.FullPath, metadata =>
            {
                metadata.LastAction = label;
                metadata.Project = row.Project;
                metadata.Version = row.Version;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compose action {Action} failed for deployment {Path}", action, row.FullPath);
            MessageBox.Show(this, $"Compose action '{action.ToCommand()}' failed.\n\n{ex.Message}", "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        if (executionResult is not null)
        {
            var failures = CollectComposeFailures(executionResult);
            if (failures.Count > 0)
            {
                var separator = Environment.NewLine + Environment.NewLine;
                var message = $"Compose action '{action.ToCommand()}' completed with errors:{Environment.NewLine}{Environment.NewLine}{string.Join(separator, failures)}{Environment.NewLine}{Environment.NewLine}See Logs for additional details.";
                MessageBox.Show(this, message, "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private Task RunDeploymentActionAsync(ComposeAction action)
    {
        return _selectedDeploymentRow is null
            ? Task.CompletedTask
            : ExecuteDeploymentActionAsync(_selectedDeploymentRow, action);
    }

    private async void OnDeploymentsUp(object sender, RoutedEventArgs e) =>
        await RunDeploymentActionAsync(ComposeAction.Up);

    private async void OnDeploymentsDown(object sender, RoutedEventArgs e) =>
        await RunDeploymentActionAsync(ComposeAction.Down);

    private async void OnDeploymentsRestart(object sender, RoutedEventArgs e) =>
        await RunDeploymentActionAsync(ComposeAction.Restart);

    private async void OnDeploymentsStop(object sender, RoutedEventArgs e) =>
        await RunDeploymentActionAsync(ComposeAction.Stop);

    private async void OnDeploymentsStart(object sender, RoutedEventArgs e) =>
        await RunDeploymentActionAsync(ComposeAction.Start);

    private async void OnDeploymentsRemove(object sender, RoutedEventArgs e)
    {
        if (_selectedDeploymentRow is null) return;
        var row = _selectedDeploymentRow;

        // Attempt to stop and remove any running containers before deleting files.
        await ExecuteDeploymentActionAsync(row, ComposeAction.Stop);
        await ExecuteDeploymentActionAsync(row, ComposeAction.Down);

        try
        {
            if (Directory.Exists(row.FullPath))
            {
                Directory.Delete(row.FullPath, recursive: true);
                _logger.LogInformation("Removed deployment at {Path}", row.FullPath);
            }
            Deployments.Remove(row);
            _selectedDeploymentRow = null;
            UpdateDeploymentButtons();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove deployment {Project} {Version}", row.Project, row.Version);
            MessageBox.Show(this, "Failed to remove deployment.\n\n" + ex.Message, "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDeploymentsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedDeploymentRow = DeploymentsList.SelectedItem as DeploymentRow;
        UpdateDeploymentButtons();
    }

    private void OnDeploymentsOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = _selectedDeploymentRow?.FullPath ?? CrossposeEnvironment.DeploymentDirectory;
            Directory.CreateDirectory(target);
            Process.Start("explorer.exe", $"\"{target}\"");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open deployments folder.");
            MessageBox.Show(this, "Unable to open deployments folder.\n\n" + ex.Message, "Crosspose", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static (string project, string version) SplitProjectAndVersion(string folderName)
    {
        var idx = folderName.LastIndexOf('-');
        if (idx > 0 && idx < folderName.Length - 1)
        {
            return (folderName[..idx], folderName[(idx + 1)..]);
        }
        return (folderName, "unknown");
    }

    private void AppendComposeLog(ComposeExecutionResult result)
    {
        if (result.DockerResult is not null)
        {
            _logStore.Write("[docker compose]");
            if (!string.IsNullOrWhiteSpace(result.DockerResult.Result.StandardOutput))
            {
                _logStore.Write(result.DockerResult.Result.StandardOutput.TrimEnd());
            }
            if (!string.IsNullOrWhiteSpace(result.DockerResult.Result.StandardError))
            {
                _logStore.Write(result.DockerResult.Result.StandardError.TrimEnd());
            }
        }

        if (result.PodmanResult is not null)
        {
            _logStore.Write("[podman compose]");
            if (!string.IsNullOrWhiteSpace(result.PodmanResult.Result.StandardOutput))
            {
                _logStore.Write(result.PodmanResult.Result.StandardOutput.TrimEnd());
            }
            if (!string.IsNullOrWhiteSpace(result.PodmanResult.Result.StandardError))
            {
                _logStore.Write(result.PodmanResult.Result.StandardError.TrimEnd());
            }
        }

        if (!result.HasAnyOutput)
        {
            _logStore.Write("[compose] No compose files were found for the selected definition.");
        }

        OnPropertyChanged(nameof(LogOutput));
    }

    private void OnDoctorClick(object sender, RoutedEventArgs e)
    {
        var path = ResolveDoctorGuiPath();
        _logger.LogInformation("Launching Crosspose Doctor GUI at {Path}", path ?? "(PATH lookup)");
        LaunchOrNotify("Crosspose Doctor", path ?? "crosspose.doctor.gui", useShell: path is null);
    }

    private void OnDekomposeClick(object sender, RoutedEventArgs e)
    {
        var path = ResolveDekomposeGuiPath();
        _logger.LogInformation("Launching Crosspose Dekompose GUI at {Path}", path ?? "(PATH lookup)");
        LaunchOrNotify(
            "Crosspose Dekompose",
            path ?? "crosspose.dekompose.gui",
            useShell: path is null,
            args: new[] { "--compress", "--infra", "--remap-ports" });
    }

    private void OnDockerDesktopClick(object sender, RoutedEventArgs e)
    {
        var path = ResolveDockerDesktopPath();
        LaunchOrNotify("Docker Desktop", path ?? "docker-desktop", useShell: path is null);
    }

    private void OnPodmanDesktopClick(object sender, RoutedEventArgs e) =>
        LaunchOrNotify("Podman Desktop", "podman-desktop");

    private void OnLogViewClick(object sender, RoutedEventArgs e)
    {
        var logWindow = new LogWindow(_logStore) { Owner = this };
        logWindow.Show();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var text = $"Crosspose GUI v{GetVersion()}\n\n" +
                   "Windows + WSL container orchestrator.\n\n" +
                   "Key features:\n" +
                   "- Unified ps for Docker (Windows) and Podman (WSL)\n" +
                   "- Container, image, and volume views\n" +
                   "- Launch Doctor and Dekompose tools\n\n" +
                   "CLI equivalents:\n" +
                   "  crosspose --help\n" +
                   "  crosspose --version\n";
        var about = new AboutWindow(text) { Owner = this };
        about.ShowDialog();
    }

    private void OnOpenDoctorFromError(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        OnDoctorClick(sender, e);

    private void LaunchOrNotify(string friendlyName, string processName, bool useShell = true, IEnumerable<string>? args = null)
    {
        _logger.LogInformation("Attempting to launch {FriendlyName} via {Process}", friendlyName, processName);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = processName,
                UseShellExecute = useShell
            };
            if (args != null)
            {
                psi.Arguments = string.Join(" ", args.Select(a =>
                    a.Contains(' ') ? $"\"{a}\"" : a));
            }

            if (File.Exists(processName))
            {
                var exeDir = Path.GetDirectoryName(processName)!;
                // For Docker Desktop under resources, prefer starting in the Docker root (parent of resources)
                if (exeDir.Contains(Path.Combine("Docker", "resources"), StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Directory.GetParent(exeDir)?.Parent?.FullName ?? Directory.GetParent(exeDir)?.FullName;
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        exeDir = parent;
                    }
                }

                psi.WorkingDirectory = exeDir;
                psi.UseShellExecute = true;
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch {FriendlyName}", friendlyName);
            MessageBox.Show(
                this,
                $"{friendlyName} could not be launched. Ensure it is installed and on PATH.\n\n{ex.Message}",
                "Crosspose",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private string? ResolveDoctorGuiPath()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Crosspose.Doctor.Gui.exe");
        if (File.Exists(local)) return local;
        _logger.LogWarning("Crosspose.Doctor.Gui.exe not found in output folder; falling back to PATH.");
        return null;
    }

    private string? ResolveDekomposeGuiPath()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Crosspose.Dekompose.Gui.exe");
        if (File.Exists(local)) return local;
        _logger.LogWarning("Crosspose.Dekompose.Gui.exe not found in output folder; falling back to PATH.");
        return null;
    }

    private static int GetRefreshIntervalSeconds() => CrossposeEnvironment.GuiRefreshIntervalSeconds;

    private string? ResolveDockerDesktopPath()
    {
        var path = CrossposeEnvironment.Path;
        if (string.IsNullOrWhiteSpace(path)) return null;

        foreach (var segment in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var dockerExe = Path.Combine(trimmed, "docker.exe");
            if (!File.Exists(dockerExe)) continue;

            var current = new DirectoryInfo(Path.GetDirectoryName(dockerExe)!);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "Docker Desktop.exe");
                if (File.Exists(candidate))
                {
                    // If found under resources, try parent Docker root first
                    if (candidate.Contains(Path.Combine("Docker", "resources"), StringComparison.OrdinalIgnoreCase))
                    {
                        var rootCandidate = Directory.GetParent(current.FullName)?.FullName;
                        if (!string.IsNullOrWhiteSpace(rootCandidate))
                        {
                            var top = Path.Combine(rootCandidate, "Docker Desktop.exe");
                            if (File.Exists(top))
                            {
                                _logger.LogInformation("Found Docker Desktop at {Path}", top);
                                return top;
                            }
                        }
                    }

                    _logger.LogInformation("Found Docker Desktop at {Path}", candidate);
                    return candidate;
                }
                current = current.Parent;
            }
        }

        _logger.LogWarning("Docker Desktop not found via PATH search; falling back to process name.");
        return null;
    }

    private void OnContainerDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ContainersTree.SelectedItem is ContainerRow row)
        {
            var details = new ContainerDetailsWindow(row, _loggerFactory, _logStore) { Owner = this };
            details.Show();
            e.Handled = true;
        }
    }

    private static string GetVersion() =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
}

    public enum ContainerExitState
    {
        Unknown,
        Running,
        ExitedSuccess,
        ExitedFailure,
        ExitedUnknown
    }

public class ContainerRow : INotifyPropertyChanged
{
        public string UniqueId { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string HostPlatform { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        public string Ports { get; set; } = string.Empty;
        private string _state = string.Empty;
        public string State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged(nameof(State));
                    UpdateDisplayState();
                }
            }
        }
        public string Status { get; set; } = string.Empty;
        private ContainerExitState _exitState = ContainerExitState.Unknown;
        public ContainerExitState ExitState
        {
            get => _exitState;
            set
            {
                if (_exitState != value)
                {
                    _exitState = value;
                    OnPropertyChanged(nameof(ExitState));
                    UpdateDisplayState();
                }
            }
        }
        public string Project { get; set; } = string.Empty;
        private string _displayState = "Unknown";
        public string DisplayState
        {
            get => _displayState;
            private set
            {
                if (_displayState != value)
                {
                    _displayState = value;
                    OnPropertyChanged(nameof(DisplayState));
                }
            }
        }

        private void UpdateDisplayState()
        {
            if (ExitState == ContainerExitState.ExitedFailure)
            {
                DisplayState = "errored";
                return;
            }

            if (ExitState == ContainerExitState.ExitedSuccess)
            {
                DisplayState = "exited";
                return;
            }

            DisplayState = string.IsNullOrWhiteSpace(State) ? "Unknown" : State;
        }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(ActionLabel));
                OnPropertyChanged(nameof(State));
            }
        }
    }

    public string ActionLabel => IsRunning ? "Stop" : "Start";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

    internal sealed record GroupedEntries(string Key, List<ContainerRow> Rows);

public class ProjectGroupRow
{
    public string Name { get; set; } = string.Empty;
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<ContainerRow> Containers { get; } = new();
}

public class ImageRow : INotifyPropertyChanged
{
    public string HostPlatform { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string State { get; set; } = "available";
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class VolumeRow : INotifyPropertyChanged
{
    public string HostPlatform { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string State { get; set; } = "available";
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ProjectEntry
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public DateTime LastWriteTime { get; set; }
    public string LastModified => LastWriteTime.ToString("g");
    public int FileCount { get; set; }
}


public class DeploymentRow : INotifyPropertyChanged
{
    public string Project { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;

    private string _platformStatus = "Unknown";
    public string PlatformStatus
    {
        get => _platformStatus;
        set
        {
            if (_platformStatus != value)
            {
                _platformStatus = value;
                OnPropertyChanged(nameof(PlatformStatus));
            }
        }
    }

    private string _lastAction = "None";
    public string LastAction
    {
        get => _lastAction;
        set
        {
            if (_lastAction != value)
            {
                _lastAction = value;
                OnPropertyChanged(nameof(LastAction));
            }
        }
    }

    private string _lastUpdated = string.Empty;
    public string LastUpdated
    {
        get => _lastUpdated;
        set
        {
            if (_lastUpdated != value)
            {
                _lastUpdated = value;
                OnPropertyChanged(nameof(LastUpdated));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
