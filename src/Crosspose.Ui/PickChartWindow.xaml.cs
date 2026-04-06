using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging;
using Crosspose.Core.Logging.Internal;
using Crosspose.Core.Orchestration;
using Crosspose.Core.Sources;
using Crosspose.Doctor.Core.Checks;
using Microsoft.Extensions.Logging;

namespace Crosspose.Ui;

public partial class PickChartWindow : Window, INotifyPropertyChanged
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ProcessRunner _runner;
    private readonly HelmClient _helm;
    private readonly OciRegistryStore _ociStore;
    private readonly HelmRepositoryStore _helmRepoStore;
    private readonly InMemoryLogStore _logStore;

    public ObservableCollection<ChartSourceListItem> ChartSources { get; } = new();
    public ObservableCollection<HelmChartSearchEntry> Charts { get; } = new();
    public ObservableCollection<SourceVersion> ChartVersions { get; } = new();

    public string? PulledChartPath { get; private set; }

    private string? _postPullValuesPath;
    private string? _postPullDekomposeConfigPath;

    public string LogOutput => _logStore.ReadAll();

    private bool _isLogExpanded;
    public bool IsLogExpanded
    {
        get => _isLogExpanded;
        set { _isLogExpanded = value; OnPropertyChanged(nameof(IsLogExpanded)); }
    }

    private ChartSourceListItem? _selectedSource;
    public ChartSourceListItem? SelectedSource
    {
        get => _selectedSource;
        set { _selectedSource = value; OnPropertyChanged(nameof(SelectedSource)); _ = LoadChartsAsync(); }
    }

    private HelmChartSearchEntry? _selectedChart;
    public HelmChartSearchEntry? SelectedChart
    {
        get => _selectedChart;
        set { _selectedChart = value; OnPropertyChanged(nameof(SelectedChart)); _ = LoadVersionsAsync(); }
    }

    private SourceVersion? _selectedVersion;
    public SourceVersion? SelectedVersion
    {
        get => _selectedVersion;
        set { _selectedVersion = value; OnPropertyChanged(nameof(SelectedVersion)); UpdatePullButton(); }
    }

    public PickChartWindow()
    {
        _logStore = new InMemoryLogStore();
        _logStore.OnWrite += _ => OnPropertyChanged(nameof(LogOutput));
        _loggerFactory = CrossposeLoggerFactory.Create(LogLevel.Information, _logStore);
        _logger = _loggerFactory.CreateLogger("crosspose.pickchartwindow");
        _runner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>());
        _helm = new HelmClient(_runner, _loggerFactory.CreateLogger<HelmClient>());
        _ociStore = new OciRegistryStore(_loggerFactory.CreateLogger<OciRegistryStore>());
        _helmRepoStore = new HelmRepositoryStore(_loggerFactory.CreateLogger<HelmRepositoryStore>());

        DataContext = this;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Overlay.Message = "Initializing Helm...";
        Overlay.Visibility = Visibility.Visible;
        try
        {
            await _helm.EnsureHelmAsync();
            await _helm.RepoUpdateAsync();
            await LoadSourcesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Helm initialization failed");
            IsLogExpanded = true;
        }
        finally
        {
            Overlay.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadSourcesAsync()
    {
        ChartSources.Clear();
        foreach (var oci in _ociStore.GetAll())
        {
            ChartSources.Add(new ChartSourceListItem
            {
                Name = oci.Name, Url = oci.Address, IsOci = true,
                Username = oci.Username, Password = oci.Password,
                BearerToken = oci.BearerToken, Filter = oci.Filter
            });
        }
        var repos = await _helm.RepoListAsync();
        foreach (var r in repos)
        {
            ChartSources.Add(new ChartSourceListItem
            {
                Name = r.Name, Url = r.Url, IsOci = false,
                Filter = _helmRepoStore.GetFilter(r.Name)
            });
        }
    }

    private async Task LoadChartsAsync()
    {
        ChartCombo.IsEnabled = false;
        Charts.Clear();
        ChartVersions.Clear();
        SelectedVersion = null;
        UpdatePullButton();
        if (SelectedSource is null) return;

        Overlay.Message = $"Loading charts from {SelectedSource.Name}...";
        Overlay.Visibility = Visibility.Visible;
        try
        {
            var auth = new SourceAuth(SelectedSource.Username, SelectedSource.Password, SelectedSource.BearerToken);
            if (SelectedSource.IsOci)
            {
                var ociClient = new OciSourceClient(SelectedSource.Url, _logger) { NameFilter = SelectedSource.Filter };
                var result = await ociClient.ListAsync(auth);
                if (result.IsSuccess)
                {
                    AcrAuthBanner.Visibility = Visibility.Collapsed;
                    foreach (var c in result.Items)
                        Charts.Add(new HelmChartSearchEntry { Name = $"{SelectedSource.Name}/{c.Name}", Description = "OCI chart" });
                    if (IsAzureContainerRegistry(SelectedSource.Url))
                        await TryAttachAcrTokenAsync(SelectedSource);
                }
                else if (IsAzureContainerRegistry(SelectedSource.Url) &&
                         result.Message?.Contains("authentication", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogError("OCI list failed: {Message}", result.Message);
                    AcrAuthBanner.Visibility = Visibility.Visible;
                    AcrReauthButton.IsEnabled = true;
                    IsLogExpanded = true;
                }
                else
                {
                    _logger.LogError("OCI list failed: {Message}", result.Message);
                    IsLogExpanded = true;
                }
            }
            else
            {
                var helmClient = new HelmSourceClient(SelectedSource.Url, _logger, _helm, SelectedSource.Name);
                var result = await helmClient.ListAsync(auth);
                if (result.IsSuccess)
                {
                    var filtered = string.IsNullOrWhiteSpace(SelectedSource.Filter)
                        ? result.Items
                        : result.Items.Where(c => c.Name.Contains(SelectedSource.Filter, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var c in filtered)
                        Charts.Add(new HelmChartSearchEntry { Name = c.Name, Description = c.Description ?? string.Empty });
                }
                else
                {
                    _logger.LogError("Helm list failed: {Message}", result.Message);
                    IsLogExpanded = true;
                }
            }
            ChartCombo.IsEnabled = Charts.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load charts");
            IsLogExpanded = true;
        }
        finally { Overlay.Visibility = Visibility.Collapsed; }
    }

    private async Task LoadVersionsAsync()
    {
        VersionCombo.IsEnabled = false;
        ChartVersions.Clear();
        SelectedVersion = null;
        UpdatePullButton();
        if (SelectedSource is null || SelectedChart is null) return;

        Overlay.Message = $"Loading versions for {SelectedChart.Name}...";
        Overlay.Visibility = Visibility.Visible;
        try
        {
            var auth = new SourceAuth(SelectedSource.Username, SelectedSource.Password, SelectedSource.BearerToken);
            if (SelectedSource.IsOci)
            {
                var ociClient = new OciSourceClient(SelectedSource.Url, _logger);
                var repoName = SelectedChart.Name.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
                var result = await ociClient.ListVersionsAsync(repoName, auth);
                if (result.IsSuccess)
                    foreach (var v in result.Versions.OrderByDescending(v => v.CreatedAt ?? DateTimeOffset.MinValue))
                        ChartVersions.Add(v);
                else
                {
                    _logger.LogError("Failed to load OCI versions: {Message}", result.Message);
                    IsLogExpanded = true;
                }
            }
            else
            {
                var helmClient = new HelmSourceClient(SelectedSource.Url, _logger, _helm, SelectedSource.Name);
                var result = await helmClient.ListVersionsAsync(SelectedChart.Name);
                if (result.IsSuccess)
                    foreach (var v in result.Versions)
                        ChartVersions.Add(v);
                else
                {
                    _logger.LogError("Failed to load chart versions: {Message}", result.Message);
                    IsLogExpanded = true;
                }
            }

            if (ChartVersions.Count > 0)
            {
                SelectedVersion = ChartVersions[0];
                VersionCombo.IsEnabled = true;
            }
            UpdatePullButton();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load versions");
            IsLogExpanded = true;
        }
        finally { Overlay.Visibility = Visibility.Collapsed; }
    }

    private async void OnAddSourceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddChartSourceWindow(_helm, _ociStore, _helmRepoStore, _runner, _loggerFactory,
            _loggerFactory.CreateLogger<AddChartSourceWindow>())
        { Owner = this };
        if (dialog.ShowDialog() == true)
            await LoadSourcesAsync();
    }

    private async void OnPullClick(object sender, RoutedEventArgs e)
    {
        if (SelectedChart is null) return;

        var chartRef = BuildChartRef();
        var version = SelectedVersion?.Tag;
        var dest = CrossposeEnvironment.HelmChartsDirectory;
        var auth = SelectedSource is not null
            ? new SourceAuth(SelectedSource.Username, SelectedSource.Password, SelectedSource.BearerToken)
            : null;

        Overlay.Message = "Pulling chart...";
        Overlay.Visibility = Visibility.Visible;
        try
        {
            var path = await _helm.PullAsync(chartRef, version, dest, auth);
            if (path is not null)
            {
                path = RenameWithOciPrefix(path, chartRef);
                PulledChartPath = path;
                // Show file association section instead of closing immediately
                PostPullSection.Visibility = Visibility.Visible;
                PullButton.Visibility = Visibility.Collapsed;
                CancelButton.Content = "Done";
                Title = "Pull Helm Chart — Done";
            }
            else
            {
                _logger.LogError("Pull failed for {Chart} {Version} — check credentials and Helm output above", chartRef, version);
                IsLogExpanded = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pull error for {Chart}", chartRef);
            IsLogExpanded = true;
        }
        finally
        {
            Overlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnAcrReauthClick(object sender, RoutedEventArgs e)
    {
        if (SelectedSource is null) return;
        AcrReauthButton.IsEnabled = false;
        AcrAuthBannerText.Text = "Re-authenticating...";
        Overlay.Message = "Re-authenticating with Azure Container Registry...";
        Overlay.Visibility = Visibility.Visible;
        try
        {
            var registryName = new Uri(SelectedSource.Url).Host.Split('.')[0];
            var acrCheck = new AzureAcrAuthWinCheck(registryName);
            var fix = await acrCheck.FixAsync(_runner, _logger, CancellationToken.None);
            if (fix.Succeeded)
            {
                _logger.LogInformation("ACR re-authentication succeeded.");
                OciSourceClient.ClearTokenCache();
                AcrAuthBanner.Visibility = Visibility.Collapsed;
                await TryAttachAcrTokenAsync(SelectedSource);
                await LoadChartsAsync();
            }
            else
            {
                _logger.LogError("ACR re-authentication failed: {Message}", fix.Message);
                AcrAuthBannerText.Text = $"Re-authentication failed: {fix.Message}";
                AcrReauthButton.IsEnabled = true;
                IsLogExpanded = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ACR re-authentication error");
            AcrAuthBannerText.Text = "Re-authentication failed — see log for details.";
            AcrReauthButton.IsEnabled = true;
            IsLogExpanded = true;
        }
        finally
        {
            Overlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (PulledChartPath is not null)
        {
            CopyPostPullFiles();
            DialogResult = true;
        }
        Close();
    }

    private void OnPostPullBrowseValuesClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select values file",
            Filter = "YAML files|*.yaml;*.yml|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _postPullValuesPath = dlg.FileName;
            ValuesFileLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
        }
    }

    private void OnPostPullBrowseDekomposeConfigClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select dekompose config",
            Filter = "Dekompose YAML|*.dekompose.yml;*.dekompose.yaml;*.yml;*.yaml|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _postPullDekomposeConfigPath = dlg.FileName;
            DekomposeConfigLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
        }
    }

    private void CopyPostPullFiles()
    {
        if (PulledChartPath is null) return;
        var baseName = Path.GetFileNameWithoutExtension(PulledChartPath);
        var dir = Path.GetDirectoryName(PulledChartPath)!;
        if (_postPullValuesPath is not null)
            File.Copy(_postPullValuesPath, Path.Combine(dir, baseName + ".values.yaml"), overwrite: true);
        if (_postPullDekomposeConfigPath is not null)
            File.Copy(_postPullDekomposeConfigPath, Path.Combine(dir, baseName + ".dekompose.yml"), overwrite: true);
    }

    private void OnLogClearRequested(object sender, EventArgs e)
    {
        _logStore.Clear();
        OnPropertyChanged(nameof(LogOutput));
    }

    private string BuildChartRef()
    {
        if (SelectedSource!.IsOci)
        {
            try
            {
                var host = new Uri(SelectedSource.Url).Host;
                var repo = SelectedChart!.Name.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
                return $"oci://{host}/{repo}";
            }
            catch { /* fall through */ }
        }
        return SelectedChart!.Name;
    }

    /// Renames a pulled tgz to embed the OCI repo sub-path in the filename so the chart
    /// name is unambiguous for rule matching (e.g. "helm-platform-9.2.486.tgz" instead of
    /// the bare "platform-9.2.486.tgz" that helm writes). Returns the new path, or the
    /// original path if the rename isn't applicable or fails.
    /// Renames a pulled tgz to embed the OCI parent path in the filename so the chart name
    /// is unambiguous for rule matching. Only the path segments above the chart name are
    /// prepended — the chart name itself is already in the filename.
    /// e.g. "platform-9.2.486.tgz" pulled from "oci://acr/helm/platform" → "helm-platform-9.2.486.tgz"
    private static string RenameWithOciPrefix(string tgzPath, string chartRef)
    {
        if (!chartRef.StartsWith("oci://", StringComparison.OrdinalIgnoreCase)) return tgzPath;
        try
        {
            // "oci://acr.azurecr.io/helm/platform" → repoPath = "helm/platform"
            var withoutScheme = chartRef["oci://".Length..];
            var firstSlash = withoutScheme.IndexOf('/');
            if (firstSlash < 0) return tgzPath;
            var repoPath = withoutScheme[(firstSlash + 1)..]; // "helm/platform"

            // Parent path only — the chart name is already the tgz filename stem.
            var lastSlash = repoPath.LastIndexOf('/');
            if (lastSlash < 0) return tgzPath; // repo root, nothing to prefix
            var parentPrefix = repoPath[..lastSlash].Replace('/', '-'); // "helm"
            if (string.IsNullOrWhiteSpace(parentPrefix)) return tgzPath;

            var dir = Path.GetDirectoryName(tgzPath)!;
            var fileName = Path.GetFileName(tgzPath);
            // Avoid double-prefixing on re-pull
            if (fileName.StartsWith(parentPrefix + "-", StringComparison.OrdinalIgnoreCase)) return tgzPath;

            var newPath = Path.Combine(dir, parentPrefix + "-" + fileName);
            File.Move(tgzPath, newPath, overwrite: true);
            return newPath;
        }
        catch { return tgzPath; }
    }

    private async Task TryAttachAcrTokenAsync(ChartSourceListItem source)
    {
        try
        {
            var registryName = new Uri(source.Url).Host.Split('.')[0];
            var acrCheck = new AzureAcrAuthWinCheck(registryName);
            var token = await acrCheck.GetAccessTokenAsync(_runner, _logger, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(token))
            {
                source.Username = "00000000-0000-0000-0000-000000000000";
                source.Password = token;
                source.BearerToken = token;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to attach ACR token for {Url}", source.Url);
        }
    }

    private static bool IsAzureContainerRegistry(string url) =>
        url.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase);

    private void UpdatePullButton() =>
        PullButton.IsEnabled = SelectedChart is not null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
