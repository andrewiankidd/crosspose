using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Crosspose.Core.Logging.Internal;

namespace Crosspose.Gui;

public partial class LogWindow : Window
{
    private readonly InMemoryLogStore _store;
    private readonly List<string> _lines = new();

    public LogWindow(InMemoryLogStore store)
    {
        InitializeComponent();
        _store = store;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lines.AddRange(_store.Snapshot());
        ApplyFilter();
        LogBox.ScrollToEnd();
        _store.OnWrite += HandleWrite;
    }

    private void HandleWrite(string line)
    {
        Dispatcher.Invoke(() =>
        {
            _lines.Add(line);
            ApplyFilter(scrollToEnd: string.IsNullOrWhiteSpace(FilterBox.Text));
        });
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _store.Clear();
        _lines.Clear();
        LogBox.Text = string.Empty;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _store.OnWrite -= HandleWrite;
    }

    private void ApplyFilter(bool scrollToEnd = true)
    {
        var filter = FilterBox.Text.Trim();
        LogBox.Text = string.IsNullOrEmpty(filter)
            ? string.Join(System.Environment.NewLine, _lines)
            : string.Join(System.Environment.NewLine,
                _lines.Where(l => l.Contains(filter, System.StringComparison.OrdinalIgnoreCase)));

        if (scrollToEnd)
            LogBox.ScrollToEnd();
    }
}
