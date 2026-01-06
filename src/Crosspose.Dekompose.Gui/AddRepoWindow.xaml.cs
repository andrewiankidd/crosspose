using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Crosspose.Core.Orchestration;
using Crosspose.Core.Sources;
using Crosspose.Core.Diagnostics;
using Crosspose.Doctor.Checks;
using Microsoft.Extensions.Logging;

namespace Crosspose.Dekompose.Gui;

public partial class AddChartSourceWindow : Window
{
    private readonly HelmClient _helm;
    private readonly OciRegistryStore _ociStore;
    private readonly HelmRepositoryStore _helmRepoStore;
    private readonly ILogger _logger;
    private readonly Action<string> _log;
    private readonly ProcessRunner _runner;
    private readonly ILoggerFactory _loggerFactory;

    public bool AddedHelm { get; private set; }
    public bool AddedOci { get; private set; }

    public AddChartSourceWindow(HelmClient helm, OciRegistryStore ociStore, HelmRepositoryStore helmRepoStore, ProcessRunner runner, ILoggerFactory loggerFactory, ILogger logger, Action<string>? logAction = null)
    {
        InitializeComponent();
        _helm = helm;
        _ociStore = ociStore;
        _helmRepoStore = helmRepoStore;
        _logger = logger;
        _log = logAction ?? (_ => { });
        _runner = runner;
        _loggerFactory = loggerFactory;
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var address = AddressBox.Text?.Trim();
        var user = UserBox.Text?.Trim();
        var pass = PassBox.Password;
        var filter = FilterBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(address))
        {
            StatusText.Text = "Address is required.";
            return;
        }

        _log($"[AddSource] Requested add for address '{address}'");

        AddButton.IsEnabled = false;
        StatusText.Text = "Detecting chart source type...";
        try
        {
            var auth = new SourceAuth(user, pass);

            var helmSource = new HelmSourceClient(address, _logger, _helm, sourceName: address);
            var helmDetect = await helmSource.DetectAsync(auth);
            _log($"[AddSource] Helm detection result: {helmDetect.IsDetected} {helmDetect.Message}");
            if (helmDetect.IsDetected)
            {
                var name = DeriveNameWithFilter(helmSource.SourceName, filter);
                var result = await _helm.RepoAddAsync(name, helmSource.SourceUrl, user, pass);
                if (!result.IsSuccess)
                {
                    StatusText.Text = $"Helm source add failed: {result.StandardError}";
                    _log($"[AddSource] Helm source add failed: {result.StandardError}");
                    System.Windows.MessageBox.Show(this, StatusText.Text, "Add failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                AddedHelm = true;
                _helmRepoStore.SetFilter(name, filter);
                StatusText.Text = $"Added Helm chart source '{name}'.";
                _log($"[AddSource] Added Helm chart source '{name}'");
                DialogResult = true;
                Close();
                return;
            }

            _log("[AddSource] Helm not detected, checking OCI catalog...");
            var ociSource = new OciSourceClient(address, _logger);
            var ociDetect = await ociSource.DetectAsync(auth);
            _log($"[AddSource] OCI detection result: {ociDetect.IsDetected} {ociDetect.Message}");
            if (ociDetect.RequiresAuth && ociSource.SourceUrl.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase))
            {
                if (!await EnsureAzureAuthAsync(ociSource.SourceUrl))
                {
                    StatusText.Text = ociDetect.Message ?? "Azure ACR authentication required.";
                    System.Windows.MessageBox.Show(this, StatusText.Text, "Authentication required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (ociDetect.IsDetected || (ociDetect.RequiresAuth && ociSource.SourceUrl.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase)))
            {
                var name = DeriveNameWithFilter(ociSource.SourceName, filter);
                _log($"[AddSource] Detected OCI registry, storing as '{name}'");
                var bearer = user == "00000000-0000-0000-0000-000000000000" && !string.IsNullOrWhiteSpace(pass)
                    ? pass
                    : null;
                _ociStore.AddOrUpdate(new OciRegistryEntry
                {
                    Name = name,
                    Address = ociSource.SourceUrl,
                    Username = string.IsNullOrWhiteSpace(user) ? null : user,
                    Password = string.IsNullOrWhiteSpace(pass) ? null : pass,
                    BearerToken = bearer,
                    Filter = string.IsNullOrWhiteSpace(filter) ? null : filter
                });
                AddedOci = true;
                StatusText.Text = $"Added OCI chart source '{name}'.";
                _log($"[AddSource] Added OCI chart source '{name}'");
                if (ociSource.SourceUrl.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase))
                {
                    var registryName = ExtractRegistryName(ociSource.SourceUrl);
                    var azCliKey = new AzureCliCheck().AdditionalKey;
                    var winKey = new AzureAcrAuthWinCheck(registryName).AdditionalKey;
                    var linKey = new AzureAcrAuthLinCheck(registryName).AdditionalKey;
                    DoctorCheckPersistence.EnsureAdditionalChecks(azCliKey, winKey, linKey);
                }
                DialogResult = true;
                Close();
                return;
            }
            else if (ociDetect.RequiresAuth && !string.IsNullOrWhiteSpace(ociDetect.Message))
            {
                StatusText.Text = ociDetect.Message;
                System.Windows.MessageBox.Show(this, ociDetect.Message, "Authentication required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var message = "Could not detect Helm or OCI registry at this address.";
            StatusText.Text = message;
            _log($"[AddSource] {message}");
            System.Windows.MessageBox.Show(this, message, "Repository not detected", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add repository.");
            _log($"[AddSource] Error: {ex.Message}");
            StatusText.Text = $"Error: {ex.Message}";
            System.Windows.MessageBox.Show(this, StatusText.Text, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AddButton.IsEnabled = true;
        }
    }

    private async Task<bool> EnsureAzureAuthAsync(string url)
    {
        var registryName = ExtractRegistryName(url);
        _log($"[AddSource] Azure ACR auth required for {registryName}. Running additional checks.");

        var azCli = new AzureCliCheck();
        var azResult = await azCli.RunAsync(_runner, _loggerFactory.CreateLogger(azCli.Name), CancellationToken.None);
        if (!azResult.IsSuccessful)
        {
            var prompt = "Azure CLI is required. Run fix now?";
            if (System.Windows.MessageBox.Show(this, prompt, "Azure CLI required", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var fix = await azCli.FixAsync(_runner, _loggerFactory.CreateLogger(azCli.Name), CancellationToken.None);
                if (!fix.Succeeded)
                {
                    _log($"[AddSource] Azure CLI fix failed: {fix.Message}");
                    StatusText.Text = fix.Message;
                    return false;
                }
            }
            else
            {
                StatusText.Text = azResult.Message;
                return false;
            }
        }

        var acrCheck = new AzureAcrAuthWinCheck(registryName);
        var acrResult = await acrCheck.RunAsync(_runner, _loggerFactory.CreateLogger(acrCheck.Name), CancellationToken.None);
        if (acrResult.IsSuccessful)
        {
            var linKey = new AzureAcrAuthLinCheck(registryName).AdditionalKey;
            DoctorCheckPersistence.EnsureAdditionalChecks(azCli.AdditionalKey, acrCheck.AdditionalKey, linKey);
            return true;
        }

        var acrPrompt = $"Authenticate to ACR '{registryName}' now?";
        if (System.Windows.MessageBox.Show(this, acrPrompt, "Azure ACR auth", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var fix = await acrCheck.FixAsync(_runner, _loggerFactory.CreateLogger(acrCheck.Name), CancellationToken.None);
            if (!fix.Succeeded)
            {
                _log($"[AddSource] ACR auth fix failed: {fix.Message}");
                StatusText.Text = fix.Message;
                return false;
            }
            await TryAttachAcrTokenAsync(registryName);
            var linKey = new AzureAcrAuthLinCheck(registryName).AdditionalKey;
            DoctorCheckPersistence.EnsureAdditionalChecks(azCli.AdditionalKey, acrCheck.AdditionalKey, linKey);
            return true;
        }

        StatusText.Text = acrResult.Message;
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
                UserBox.Text = user;
                PassBox.Password = token;
                _log($"[AddSource] Retrieved ACR token for {registryName}; using bearer credentials.");
            }
        }
        catch (Exception ex)
        {
            _log($"[AddSource] Failed to attach ACR token: {ex.Message}");
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

    private static string DeriveNameWithFilter(string baseName, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return baseName;
        var safe = new string(filter
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        safe = safe.Trim('-');
        if (string.IsNullOrWhiteSpace(safe))
        {
            return baseName;
        }

        if (safe.Length > 32)
        {
            safe = safe[..32];
        }

        return $"{baseName}-{safe}";
    }
}
