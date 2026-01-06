using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.Logging;
using Crosspose.Core.Configuration;
using Crosspose.Core.Orchestration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging;
using Crosspose.Core.Logging.Internal;
using Crosspose.Core.Sources;
using Crosspose.Doctor.Checks;

namespace Crosspose.Dekompose.Gui;

public partial class MainWindow : Window, INotifyPropertyChanged
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

    private ChartSourceListItem? _selectedSource;
    public ChartSourceListItem? SelectedSource
    {
        get => _selectedSource;
        set
        {
            _selectedSource = value;
            OnPropertyChanged(nameof(SelectedSource));
            _ = LoadChartsAsync();
        }
    }

    private HelmChartSearchEntry? _selectedChart;
    public HelmChartSearchEntry? SelectedChart
    {
        get => _selectedChart;
        set
        {
            _selectedChart = value;
            OnPropertyChanged(nameof(SelectedChart));
            _ = LoadVersionsAsync();
        }
    }

    private SourceVersion? _selectedVersion;
    private bool _suppressVersionChanges;
    public SourceVersion? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            _selectedVersion = value;
            OnPropertyChanged(nameof(SelectedVersion));
            if (!_suppressVersionChanges)
            {
                _ = LoadValuesAsync();
            }
        }
    }

    private string _valuesContent = string.Empty;
    public string ValuesContent
    {
        get => _valuesContent;
        set
        {
            _valuesContent = value;
            OnPropertyChanged(nameof(ValuesContent));
            OnPropertyChanged(nameof(ValuesHeader));
        }
    }

    private string _valuesFilePath = string.Empty;
    public string ValuesFilePath
    {
        get => _valuesFilePath;
        set
        {
            _valuesFilePath = value;
            OnPropertyChanged(nameof(ValuesFilePath));
            OnPropertyChanged(nameof(ValuesHeader));
        }
    }

    public string ValuesHeader => string.IsNullOrWhiteSpace(ValuesFilePath)
        ? "Using chart's default values"
        : $"Using user-supplied values file @ {ValuesFilePath}";

    private string _dekomposeFilePath = string.Empty;
    public string DekomposeFilePath
    {
        get => _dekomposeFilePath;
        set
        {
            _dekomposeFilePath = value;
            OnPropertyChanged(nameof(DekomposeFilePath));
            OnPropertyChanged(nameof(DekomposeHeader));
        }
    }

    public string DekomposeHeader => string.IsNullOrWhiteSpace(DekomposeFilePath)
        ? "Using crosspose.yml dekompose settings"
        : $"Merged dekompose settings from {DekomposeFilePath}";

    private bool _compressOutput;
    public bool CompressOutput
    {
        get => _compressOutput;
        set
        {
            _compressOutput = value;
            OnPropertyChanged(nameof(CompressOutput));
        }
    }

    private bool _includeInfraEstimates;
    public bool IncludeInfraEstimates
    {
        get => _includeInfraEstimates;
        set
        {
            _includeInfraEstimates = value;
            OnPropertyChanged(nameof(IncludeInfraEstimates));
        }
    }

    private bool _remapServicePorts;
    public bool RemapServicePorts
    {
        get => _remapServicePorts;
        set
        {
            _remapServicePorts = value;
            OnPropertyChanged(nameof(RemapServicePorts));
        }
    }

    private string _outputDirectory = CrossposeEnvironment.OutputDirectory;
    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            _outputDirectory = value;
            OnPropertyChanged(nameof(OutputDirectory));
        }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    public string LogOutput => _logStore.ReadAll();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private string _busyText = "Loading...";
    public string BusyText
    {
        get => _busyText;
        set
        {
            _busyText = value;
            OnPropertyChanged(nameof(BusyText));
        }
    }

    public MainWindow(string? initialOutput = null, bool compressOutput = false, bool includeInfra = false, bool remapServicePorts = false)
    {
        InitializeComponent();
        DataContext = this;

        if (!string.IsNullOrWhiteSpace(initialOutput))
        {
            _outputDirectory = initialOutput;
        }
        CompressOutput = compressOutput;
        IncludeInfraEstimates = includeInfra;
        RemapServicePorts = remapServicePorts;

        _logStore = new InMemoryLogStore();
        _logStore.OnWrite += _ => OnPropertyChanged(nameof(LogOutput));
        _loggerFactory = CrossposeLoggerFactory.Create(LogLevel.Information, _logStore);
        _logger = _loggerFactory.CreateLogger("crosspose.dekompose.gui");
        _runner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>())
        {
            OutputHandler = line => _logStore.Write(line)
        };
        _helm = new HelmClient(_runner, _loggerFactory.CreateLogger<HelmClient>());
        _ociStore = new OciRegistryStore(_loggerFactory.CreateLogger<OciRegistryStore>());
        _helmRepoStore = new HelmRepositoryStore(_loggerFactory.CreateLogger<HelmRepositoryStore>());
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeHelmAsync();
    }

    private async Task InitializeHelmAsync()
    {
        try
        {
            SetBusy("Ensuring Helm is available...");
            StatusMessage = "Ensuring Helm is available...";
            await _helm.EnsureHelmAsync();
            SetBusy("Updating Helm repos...");
            StatusMessage = "Updating Helm repos...";
            await _helm.RepoUpdateAsync();
            await LoadSourcesAsync();
            SourceCombo.IsEnabled = true;
            StatusMessage = "Ready.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Helm.");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            ClearBusy();
        }
    }

    private async Task LoadSourcesAsync()
    {
        ChartSources.Clear();
        foreach (var oci in _ociStore.GetAll())
        {
            ChartSources.Add(new ChartSourceListItem
            {
                Name = oci.Name,
                Url = oci.Address,
                IsOci = true,
                Username = oci.Username,
                Password = oci.Password,
                BearerToken = oci.BearerToken,
                Filter = oci.Filter
            });
        }

        var repos = await _helm.RepoListAsync();
        foreach (var r in repos)
        {
            ChartSources.Add(new ChartSourceListItem
            {
                Name = r.Name,
                Url = r.Url,
                IsOci = false,
                Filter = _helmRepoStore.GetFilter(r.Name)
            });
        }

        OnPropertyChanged(nameof(ChartSources));
        AppendLog($"[Sources] Loaded {ChartSources.Count} chart sources.");
        foreach (var source in ChartSources)
        {
            AppendLog($"[Sources] {source.Display} => {source.Url}");
        }
    }

    private async Task LoadChartsAsync()
    {
        ChartCombo.IsEnabled = false;
        Charts.Clear();
        ValuesContent = string.Empty;
        VersionCombo.IsEnabled = false;
        ChartVersions.Clear();
        SuppressVersionSelection(() => SelectedVersion = null);
        if (SelectedSource is null) return;

        SetBusy($"Searching charts in {SelectedSource.Name}...");
        try
        {
            StatusMessage = $"Searching charts in {SelectedSource.Name}...";
            var bearer = !string.IsNullOrWhiteSpace(SelectedSource.BearerToken)
                ? SelectedSource.BearerToken
                : (SelectedSource.Username == "00000000-0000-0000-0000-000000000000" ? SelectedSource.Password : null);
            if (!string.IsNullOrWhiteSpace(bearer))
            {
                SelectedSource.BearerToken = bearer;
            }

            var auth = new SourceAuth(SelectedSource.Username, SelectedSource.Password, bearer);
            if (SelectedSource.IsOci)
            {
                AppendLog($"[Sources] Listing OCI charts from {SelectedSource.Name} ({SelectedSource.Url})...");
                var ociClient = new OciSourceClient(SelectedSource.Url, _logger)
                {
                    NameFilter = SelectedSource.Filter
                };
                var listResult = await ociClient.ListAsync(auth);
                if (!listResult.IsSuccess && SelectedSource.Url.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"[Sources] OCI list failed with message '{listResult.Message}', attempting Azure auth workflow.");
                    var ensured = await EnsureAzureAuthForSourceAsync(SelectedSource.Url);
                    if (ensured)
                    {
                        listResult = await ociClient.ListAsync(auth);
                    }
                }
                if (!listResult.IsSuccess)
                {
                    AppendLog($"[Sources] OCI list failed: {listResult.Message}");
                    StatusMessage = listResult.Message ?? "OCI list failed.";
                    System.Windows.MessageBox.Show(this, StatusMessage, "OCI list failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    AppendLog($"[Sources] OCI response count: {listResult.Items.Count}");
                    foreach (var c in listResult.Items)
                    {
                        AppendLog($"[Sources] OCI repository: {c.Name}");
                        Charts.Add(new HelmChartSearchEntry
                        {
                            Name = $"{SelectedSource.Name}/{c.Name}",
                            Version = string.Empty,
                            Description = "OCI chart"
                        });
                    }
                }
            }
            else
            {
                AppendLog($"[Sources] Searching Helm repo {SelectedSource.Name} ({SelectedSource.Url})...");
                var helmClient = new HelmSourceClient(SelectedSource.Url, _logger, _helm, SelectedSource.Name);
                var listResult = await helmClient.ListAsync(auth);
                if (!listResult.IsSuccess)
                {
                    AppendLog($"[Sources] Helm list failed: {listResult.Message}");
                    StatusMessage = listResult.Message ?? "Helm list failed.";
                    System.Windows.MessageBox.Show(this, StatusMessage, "Helm list failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    var filteredCharts = ApplyChartFilter(listResult.Items, SelectedSource.Filter);
                    AppendLog($"[Sources] Helm response count: {filteredCharts.Count}");
                    foreach (var c in filteredCharts)
                    {
                        AppendLog($"[Sources] Helm chart: {c.Name}");
                        Charts.Add(new HelmChartSearchEntry
                        {
                            Name = c.Name,
                            Version = string.Empty,
                            Description = string.IsNullOrWhiteSpace(c.Description) ? "Helm chart" : c.Description
                        });
                    }
                }
            }
            ChartCombo.IsEnabled = Charts.Count > 0;
            StatusMessage = "Ready.";
        }
        finally
        {
            ClearBusy();
        }
    }

    private void SuppressVersionSelection(Action action)
    {
        _suppressVersionChanges = true;
        action();
        _suppressVersionChanges = false;
    }

    private async Task<bool> EnsureAzureAuthForSourceAsync(string url)
    {
        var registryName = ExtractRegistryName(url);
        AppendLog($"[Sources] Azure ACR auth required for {registryName}. Running additional checks.");

        var azCli = new AzureCliCheck();
        AppendLog("[Sources] Checking Azure CLI availability...");
        CheckResult azResult;
        try
        {
            azResult = await azCli.RunAsync(_runner, _loggerFactory.CreateLogger(azCli.Name), CancellationToken.None)
                                   .WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            AppendLog("[Sources] Azure CLI check timed out after 20 seconds.");
            StatusMessage = "Azure CLI check timed out.";
            return false;
        }
        if (!azResult.IsSuccessful)
        {
            var prompt = "Azure CLI is required. Run fix now?";
            if (System.Windows.MessageBox.Show(this, prompt, "Azure CLI required", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                AppendLog("[Sources] Running Azure CLI fix (winget)...");
                FixResult fix;
                try
                {
                    fix = await azCli.FixAsync(_runner, _loggerFactory.CreateLogger(azCli.Name), CancellationToken.None)
                                     .WaitAsync(TimeSpan.FromSeconds(120));
                }
                catch (TimeoutException)
                {
                    AppendLog("[Sources] Azure CLI fix timed out after 120 seconds.");
                    StatusMessage = "Azure CLI fix timed out.";
                    return false;
                }
                if (!fix.Succeeded)
                {
                    AppendLog($"[Sources] Azure CLI fix failed: {fix.Message}");
                    StatusMessage = fix.Message;
                    return false;
                }
                else
                {
                    AppendLog("[Sources] Azure CLI fix completed successfully.");
                }
            }
            else
            {
                StatusMessage = azResult.Message;
                AppendLog("[Sources] User declined Azure CLI installation.");
                return false;
            }
        }
        else
        {
            AppendLog($"[Sources] Azure CLI available: {azResult.Message}");
        }

        var acrCheck = new AzureAcrAuthWinCheck(registryName);
        AppendLog("[Sources] Checking ACR token availability...");
        CheckResult acrResult;
        try
        {
            acrResult = await acrCheck.RunAsync(_runner, _loggerFactory.CreateLogger(acrCheck.Name), CancellationToken.None)
                                      .WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            AppendLog("[Sources] ACR token check timed out after 60 seconds.");
            StatusMessage = "ACR token check timed out.";
            return false;
        }
        if (acrResult.IsSuccessful)
        {
            AppendLog("[Sources] ACR token already available, proceeding.");
            await TryAttachAcrTokenAsync(registryName);
            var linKey = new AzureAcrAuthLinCheck(registryName).AdditionalKey;
            DoctorCheckPersistence.EnsureAdditionalChecks(azCli.AdditionalKey, acrCheck.AdditionalKey, linKey);
            return true;
        }
        AppendLog($"[Sources] ACR token missing: {acrResult.Message}");

        var acrPrompt = $"Authenticate to ACR '{registryName}' now?";
        if (System.Windows.MessageBox.Show(this, acrPrompt, "Azure ACR auth", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            AppendLog("[Sources] Running ACR auth fix (az login + az acr login)...");
            FixResult fix;
            try
            {
                fix = await acrCheck.FixAsync(_runner, _loggerFactory.CreateLogger(acrCheck.Name), CancellationToken.None)
                                    .WaitAsync(TimeSpan.FromSeconds(180));
            }
            catch (TimeoutException)
            {
                AppendLog("[Sources] ACR auth fix timed out after 180 seconds.");
                StatusMessage = "ACR auth fix timed out.";
                return false;
            }
            if (!fix.Succeeded)
            {
                AppendLog($"[Sources] ACR auth fix failed: {fix.Message}");
                StatusMessage = fix.Message;
                return false;
            }
            else
            {
                AppendLog("[Sources] ACR auth fix completed successfully.");
            }
            await TryAttachAcrTokenAsync(registryName);
            var linKey = new AzureAcrAuthLinCheck(registryName).AdditionalKey;
            DoctorCheckPersistence.EnsureAdditionalChecks(azCli.AdditionalKey, acrCheck.AdditionalKey, linKey);
            return true;
        }

        StatusMessage = acrResult.Message;
        AppendLog("[Sources] User declined ACR authentication prompt.");
        return false;
    }

    private async Task TryAttachAcrTokenAsync(string registryName)
    {
        try
        {
            var acrCheck = new AzureAcrAuthWinCheck(registryName);
            var token = await acrCheck.GetAccessTokenAsync(_runner, _loggerFactory.CreateLogger(acrCheck.Name), CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(token))
            {
                var user = "00000000-0000-0000-0000-000000000000";
                SelectedSource!.Username = user;
                SelectedSource!.Password = token;
                SelectedSource!.BearerToken = token;
                AppendLog($"[Sources] Retrieved ACR token for {registryName}; using bearer credentials.");
            }
            else
            {
                AppendLog($"[Sources] Could not retrieve ACR token for {registryName} after auth workflow.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[Sources] Failed to attach ACR token: {ex.Message}");
        }
    }

    private static string ExtractRegistryName(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return host.Split('.')[0];
        }
        catch
        {
            return url;
        }
    }

    private async Task LoadVersionsAsync()
    {
        VersionCombo.IsEnabled = false;
        ChartVersions.Clear();
        SuppressVersionSelection(() => SelectedVersion = null);
        if (SelectedSource is null || SelectedChart is null)
        {
            await LoadValuesAsync();
            return;
        }

        var chartFull = SelectedChart.Name;
        if (File.Exists(chartFull) || Directory.Exists(chartFull))
        {
            VersionCombo.IsEnabled = false;
            await LoadValuesAsync();
            return;
        }
        SetBusy($"Loading versions for {chartFull}...");
        try
        {
            StatusMessage = $"Loading versions for {chartFull}...";
            if (SelectedSource.IsOci)
            {
                var auth = new SourceAuth(SelectedSource.Username, SelectedSource.Password, SelectedSource.BearerToken);
                var ociClient = new OciSourceClient(SelectedSource.Url, _logger);
                var repoName = chartFull.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
                var versionsResult = await ociClient.ListVersionsAsync(repoName, auth);
                if (!versionsResult.IsSuccess && SelectedSource.Url.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"[Sources] OCI version listing failed with '{versionsResult.Message}', attempting Azure auth workflow.");
                    var ensured = await EnsureAzureAuthForSourceAsync(SelectedSource.Url);
                    if (ensured)
                    {
                        versionsResult = await ociClient.ListVersionsAsync(repoName, auth);
                    }
                }

                if (!versionsResult.IsSuccess)
                {
                    var message = versionsResult.Message ?? "Failed to list chart versions.";
                    AppendLog($"[Sources] OCI version listing failed for {chartFull}: {message}");
                    StatusMessage = message;
                    System.Windows.MessageBox.Show(this, message, "Version listing failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    foreach (var version in versionsResult.Versions
                                 .OrderByDescending(v => v.CreatedAt ?? DateTimeOffset.MinValue)
                                 .ThenByDescending(v => v.Tag, StringComparer.OrdinalIgnoreCase))
                    {
                        ChartVersions.Add(version);
                    }
                }
            }
            else
            {
                var helmClient = new HelmSourceClient(SelectedSource.Url, _logger, _helm, SelectedSource.Name);
                var versionsResult = await helmClient.ListVersionsAsync(chartFull);
                if (!versionsResult.IsSuccess)
                {
                    var message = versionsResult.Message ?? "Failed to list chart versions.";
                    AppendLog($"[Sources] Helm version listing failed for {chartFull}: {message}");
                    StatusMessage = message;
                    System.Windows.MessageBox.Show(this, message, "Version listing failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    foreach (var version in versionsResult.Versions)
                    {
                        ChartVersions.Add(version);
                    }
                }
            }

            if (ChartVersions.Count > 0)
            {
                VersionCombo.IsEnabled = true;
                SuppressVersionSelection(() => SelectedVersion = ChartVersions[0]);
                await LoadValuesAsync();
                StatusMessage = "Ready.";
            }
            else
            {
                VersionCombo.IsEnabled = false;
                await LoadValuesAsync();
            }
        }
        finally
        {
            ClearBusy();
        }
    }

    private async Task LoadValuesAsync()
    {
        ValuesContent = string.Empty;
        if (SelectedSource is null || SelectedChart is null) return;

        var chartFull = SelectedChart.Name;
        var selectedVersion = SelectedVersion?.Tag;
        if (SelectedSource.IsOci)
        {
            var auth = new SourceAuth(SelectedSource.Username, SelectedSource.Password, SelectedSource.BearerToken);
            var ociClient = new OciSourceClient(SelectedSource.Url, _logger);
            var repoName = chartFull.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();

            SetBusy($"Loading values for {chartFull}...");
            try
            {
                StatusMessage = $"Validating Helm chart {chartFull}...";
                var isHelm = await ociClient.IsHelmChartAsync(repoName, auth);
                if (!isHelm)
                {
                    var msg = $"Selection '{chartFull}' is not a helm chart.";
                    AppendLog(msg);
                    StatusMessage = msg;
                    System.Windows.MessageBox.Show(this, msg, "Not a Helm chart", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ValuesContent = "# Selection is not a helm chart.\n";
                    return;
                }

                var versionText = string.IsNullOrWhiteSpace(selectedVersion) ? "latest" : selectedVersion;
                StatusMessage = $"Loading values for {chartFull} ({versionText})...";
                var valuesResult = await ociClient.GetChartValuesAsync(repoName, auth, CancellationToken.None, selectedVersion);
                if (valuesResult.Success && valuesResult.Values is not null)
                {
                    ValuesContent = valuesResult.Values;
                    StatusMessage = "Ready.";
                }
                else
                {
                    var err = valuesResult.Error ?? "Failed to load chart values.";
                    AppendLog($"[Sources] OCI values retrieval failed for {chartFull}: {err}");
                    StatusMessage = err;
                    System.Windows.MessageBox.Show(this, err, "Values retrieval failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    ValuesContent = $"# {err}\n";
                }
            }
            finally
            {
                ClearBusy();
            }
            return;
        }

        SetBusy($"Loading values for {chartFull}...");
        try
        {
            var versionText = string.IsNullOrWhiteSpace(selectedVersion) ? "latest" : selectedVersion;
            StatusMessage = $"Loading values for {chartFull} ({versionText})...";
            var values = await _helm.ShowValuesAsync(chartFull, selectedVersion);
            ValuesContent = values;
            StatusMessage = "Ready.";
        }
        finally
        {
            ClearBusy();
        }
    }

    private void OnLoadValuesClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "YAML files|*.yml;*.yaml|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            ValuesFilePath = dlg.FileName;
            ValuesContent = File.ReadAllText(dlg.FileName);
        }
    }

    private void OnLoadDekomposeConfigClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Dekompose YAML|*.dekompose.yml;*.dekompose.yaml;*.yml;*.yaml|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                CrossposeConfigurationStore.MergeDekomposeConfiguration(dlg.FileName);
                DekomposeFilePath = dlg.FileName;
                AppendLog($"[Dekompose] Merged dekompose config from {dlg.FileName} into {CrossposeConfigurationStore.ConfigPath}.");
                StatusMessage = "Merged dekompose configuration.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to merge dekompose config: {ex.Message}";
                System.Windows.MessageBox.Show(this, StatusMessage, "Dekompose config failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnBrowseOutputClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog();
        var result = dlg.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
        {
            OutputDirectory = dlg.SelectedPath;
        }
    }

    private void OnBrowseChartClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Chart packages (*.tgz)|*.tgz|YAML manifests (*.yaml;*.yml)|*.yaml;*.yml|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            var filePath = dlg.FileName;
            Charts.Clear();
            var local = new HelmChartSearchEntry
            {
                Name = filePath,
                Version = string.Empty,
                Description = "Local chart",
            };
            Charts.Add(local);
            SelectedChart = local;
            ChartCombo.IsEnabled = true;
            VersionCombo.IsEnabled = false;
            ChartVersions.Clear();
            SuppressVersionSelection(() => SelectedVersion = null);
            StatusMessage = "Local chart selected.";
        }
    }

    private async void OnDekomposeClick(object sender, RoutedEventArgs e)
    {
        if (SelectedChart is null)
        {
            StatusMessage = "Select a chart first.";
            return;
        }
        if (SelectedSource is null)
        {
            StatusMessage = "Select a chart source first.";
            return;
        }

        string chartRef = SelectedChart.Name;
        if (SelectedSource.IsOci)
        {
            try
            {
                var host = new Uri(SelectedSource.Url).Host;
                var repo = SelectedChart.Name.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                if (!string.IsNullOrWhiteSpace(repo))
                {
                    chartRef = $"oci://{host}/{repo}";
                    AppendLog($"[Dekompose] Using OCI chart reference {chartRef}");
                }
            }
            catch
            {
                // fall back to provided name if parsing fails
            }
        }

        SetBusy("Running dekompose...");
        Directory.CreateDirectory(OutputDirectory);
        var tempValues = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempValues, ValuesContent);

        var dekomposeProj = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Crosspose.Dekompose", "Crosspose.Dekompose.csproj"));
        var args = $"run --project \"{dekomposeProj}\" --chart \"{chartRef}\" --values \"{tempValues}\" --output \"{OutputDirectory}\"";
        if (!string.IsNullOrWhiteSpace(SelectedVersion?.Tag))
        {
            args += $" --chart-version \"{SelectedVersion.Tag}\"";
        }
        if (CompressOutput)
        {
            args += " --compress";
        }
        if (IncludeInfraEstimates)
        {
            args += " --infra";
        }
        if (RemapServicePorts)
        {
            args += " --remap-ports";
        }
        try
        {
            var result = await _runner.RunAsync("dotnet", args);
            if (!string.IsNullOrWhiteSpace(result.StandardOutput)) AppendLog(result.StandardOutput);
            if (!string.IsNullOrWhiteSpace(result.StandardError)) AppendLog(result.StandardError);
            StatusMessage = result.IsSuccess ? "Dekompose completed." : "Dekompose failed.";
            if (!result.IsSuccess)
            {
                var message = string.IsNullOrWhiteSpace(result.StandardError)
                    ? "Dekompose failed. See Logs for details."
                    : $"Dekompose failed:\n\n{result.StandardError}";
                System.Windows.MessageBox.Show(this, message, "Crosspose Dekompose", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            ClearBusy();
        }
    }

    private void OnRefreshReposClick(object sender, RoutedEventArgs e) => _ = InitializeHelmAsync();

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private async void OnAddSourceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddChartSourceWindow(_helm, _ociStore, _helmRepoStore, _runner, _loggerFactory, _loggerFactory.CreateLogger<AddChartSourceWindow>(), AppendLog)
        {
            Owner = this
        };
        var result = dialog.ShowDialog();
        if (result == true)
        {
            AppendLog("[AddSource] Refreshing chart sources after add...");
            await InitializeHelmAsync();
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var text = $"Crosspose Dekompose GUI v{GetVersion()}\n\n" +
                   "Render Helm charts and emit Compose-friendly outputs.\n\n" +
                   "CLI equivalent:\n" +
                   "  crosspose dekompose --help\n" +
                   "  crosspose dekompose --version\n";
        var about = new AboutWindow(text) { Owner = this };
        about.ShowDialog();
    }

    private static string GetVersion() =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    private void AppendLog(string line) => _logStore.Write(line);

    private void SetBusy(string text)
    {
        BusyText = text;
        IsBusy = true;
    }

    private void ClearBusy()
    {
        IsBusy = false;
        BusyText = "Loading...";
    }

    private void OnClearLogClick(object sender, RoutedEventArgs e)
    {
        _logStore.Clear();
        OnPropertyChanged(nameof(LogOutput));
    }

    private void OnLogClearRequested(object sender, EventArgs e)
    {
        _logStore.Clear();
        OnPropertyChanged(nameof(LogOutput));
    }

    private static IReadOnlyList<SourceChart> ApplyChartFilter(IReadOnlyList<SourceChart> charts, string? filter)
    {
        if (charts.Count == 0 || string.IsNullOrWhiteSpace(filter)) return charts;
        return charts
            .Where(c => c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ChartSourceListItem
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsOci { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? BearerToken { get; set; }
    public string? Filter { get; set; }
    public string Display => $"{Name} ({(IsOci ? "OCI" : "Helm")})";
}
