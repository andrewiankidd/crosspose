using System.Text;
using System.Windows;
using Crosspose.Core.Logging.Internal;

namespace Crosspose.Gui;

public partial class LogWindow : Window
{
    private readonly InMemoryLogStore _store;
    private readonly StringBuilder _buffer = new();

    public LogWindow(InMemoryLogStore store)
    {
        InitializeComponent();
        _store = store;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var line in _store.Snapshot())
        {
            _buffer.AppendLine(line);
        }
        LogBox.Text = _buffer.ToString();
        LogBox.ScrollToEnd();
        _store.OnWrite += HandleWrite;
    }

    private void HandleWrite(string line)
    {
        Dispatcher.Invoke(() =>
        {
            _buffer.AppendLine(line);
            LogBox.Text = _buffer.ToString();
            LogBox.ScrollToEnd();
        });
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _store.OnWrite -= HandleWrite;
    }
}
