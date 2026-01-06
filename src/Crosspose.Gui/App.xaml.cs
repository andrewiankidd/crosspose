using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Crosspose.Gui;

public partial class App : Application
{
}

public class StatusBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var state = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        return state switch
        {
            "running" => new SolidColorBrush(Color.FromRgb(34, 197, 94)), // green
            "available" => new SolidColorBrush(Color.FromRgb(34, 197, 94)), // green for images/volumes
            "paused" => new SolidColorBrush(Color.FromRgb(234, 179, 8)), // yellow
            _ => new SolidColorBrush(Color.FromRgb(239, 68, 68)) // red
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
