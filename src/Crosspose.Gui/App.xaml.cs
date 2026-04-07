using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Crosspose.Core.Configuration;

namespace Crosspose.Gui;

public partial class App : Application
{
    public static bool IsDarkMode { get; private set; } = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!AppDataLocator.IsRunningElevated)
        {
            MessageBox.Show(
                "Crosspose requires Administrator privileges to manage port proxies, " +
                "firewall rules, and Docker/WSL services.\n\n" +
                "Please relaunch from an elevated terminal:\n\n" +
                "Start-Process powershell -Verb RunAs -ArgumentList '-NoExit','-Command','dotnet run --project src/Crosspose.Gui'",
                "Crosspose — Administrator required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        var cfg = CrossposeConfigurationStore.Load();
        if (cfg.Compose.Gui.DarkMode == true)
            ApplyTheme(dark: true);
    }

    public static void ApplyTheme(bool dark)
    {
        IsDarkMode = dark;
        var dicts = Application.Current.Resources.MergedDictionaries;
        var darkDict = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Dark") == true);
        var lightDict = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Light") == true);
        if (darkDict is null || lightDict is null) return;

        dicts.Remove(darkDict);
        dicts.Remove(lightDict);
        if (dark)
        {
            dicts.Add(lightDict);
            dicts.Add(darkDict);
        }
        else
        {
            dicts.Add(darkDict);
            dicts.Add(lightDict);
        }
    }

    public static void ToggleTheme()
    {
        var dark = !IsDarkMode;
        ApplyTheme(dark);
        var cfg = CrossposeConfigurationStore.Load();
        cfg.Compose.Gui.DarkMode = dark ? true : null;
        CrossposeConfigurationStore.Save(cfg);
    }
}

public class StatusBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var state = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        return state switch
        {
            "running" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),   // green
            "available" => new SolidColorBrush(Color.FromRgb(34, 197, 94)), // green for images/volumes
            "paused" => new SolidColorBrush(Color.FromRgb(234, 179, 8)),    // amber
            "unhealthy" => new SolidColorBrush(Color.FromRgb(234, 179, 8)), // amber — running but healthcheck failing
            "starting" => new SolidColorBrush(Color.FromRgb(234, 179, 8)),  // amber — healthcheck not yet passing
            _ => new SolidColorBrush(Color.FromRgb(239, 68, 68))            // red
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotImplementedException();
}

public class PlatformIconConverter : System.Windows.Data.IValueConverter
{
    private static readonly Uri WinIcon = new("pack://application:,,,/logo_win.ico", UriKind.Absolute);
    private static readonly Uri LinIcon = new("pack://application:,,,/logo_lin.ico", UriKind.Absolute);

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var host = value?.ToString()?.Trim().ToLowerInvariant();
        var uri = host is not null && host.StartsWith("win") ? WinIcon : LinIcon;
        try
        {
            return new BitmapImage(uri);
        }
        catch
        {
            return null!;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotImplementedException();
}
