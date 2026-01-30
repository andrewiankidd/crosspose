using System.Collections.ObjectModel;
using System.Windows;
using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Doctor;
using Crosspose.Doctor.Checks;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Gui;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CheckViewModel> _items = new();
    private readonly ILoggerFactory _loggerFactory = Crosspose.Core.Logging.CrossposeLoggerFactory.Create(LogLevel.Information);
    private readonly ProcessRunner _runner;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppDataLocator.WithPortableSuffix(Title);
        _runner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>());
        ChecksList.ItemsSource = _items;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = DoctorSettings.Load();
        var checks = CheckCatalog.LoadAll(settings.AdditionalChecks);
        foreach (var check in checks)
        {
            var vm = new CheckViewModel(check);
            _items.Add(vm);

            var result = await check.RunAsync(_runner, _loggerFactory.CreateLogger(check.Name), default);
            vm.Result = result.Message;
            vm.IsSuccess = result.IsSuccessful;
            vm.IsFixEnabled = !result.IsSuccessful && check.CanFix;
        }
    }

    private async void OnFixClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CheckViewModel vm }) return;
        if (!vm.Check.CanFix) return;

        var dialog = new FixWindow(vm.Check, _loggerFactory);
        var result = dialog.ShowDialog();
        if (result == true)
        {
            vm.Result = dialog.FinalMessage;
            vm.IsSuccess = dialog.Success;
            vm.IsFixEnabled = !dialog.Success;
            if (dialog.Success)
            {
                var verify = await vm.Check.RunAsync(_runner, _loggerFactory.CreateLogger(vm.Check.Name), default);
                vm.Result = verify.Message;
                vm.IsSuccess = verify.IsSuccessful;
                vm.IsFixEnabled = !verify.IsSuccessful && vm.Check.CanFix;
            }
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var text = $"Crosspose Doctor GUI v{GetVersion()}\n\n" +
                   "Prerequisite checker for Crosspose workloads.\n\n" +
                   "CLI equivalents:\n" +
                   "  crosspose doctor --help\n" +
                   "  crosspose doctor --version\n";
        var about = new AboutWindow(text) { Owner = this };
        about.ShowDialog();
    }

    private static string GetVersion() =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnChecksListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ChecksList.SelectedItem is not CheckViewModel vm)
        {
            return;
        }

        var status = vm.IsSuccess ? "Success" : "Needs Attention";
        var message = $"{vm.Description}\n\nResult ({status}):\n{vm.Result}";
        MessageBox.Show(this, message, "Crosspose Doctor", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

public class CheckViewModel : DependencyObject
{
    public CheckViewModel(ICheckFix check)
    {
        Check = check;
        Name = check.Name;
        Description = check.Description;
        Result = "Pending...";
        IsFixEnabled = check.CanFix;
    }

    public ICheckFix Check { get; }

    public string Name
    {
        get => (string)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public string Result
    {
        get => (string)GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsSuccess
    {
        get => (bool)GetValue(IsSuccessProperty);
        set => SetValue(IsSuccessProperty, value);
    }

    public bool IsFixEnabled
    {
        get => (bool)GetValue(IsFixEnabledProperty);
        set => SetValue(IsFixEnabledProperty, value);
    }

    public static readonly DependencyProperty NameProperty =
        DependencyProperty.Register(nameof(Name), typeof(string), typeof(CheckViewModel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ResultProperty =
        DependencyProperty.Register(nameof(Result), typeof(string), typeof(CheckViewModel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(CheckViewModel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsSuccessProperty =
        DependencyProperty.Register(nameof(IsSuccess), typeof(bool), typeof(CheckViewModel), new PropertyMetadata(false));

    public static readonly DependencyProperty IsFixEnabledProperty =
        DependencyProperty.Register(nameof(IsFixEnabled), typeof(bool), typeof(CheckViewModel), new PropertyMetadata(false));
}
