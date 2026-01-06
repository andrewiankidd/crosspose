using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Crosspose.Ui;

public partial class LogViewerControl : UserControl
{
    public LogViewerControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty LogTextProperty =
        DependencyProperty.Register(nameof(LogText), typeof(string), typeof(LogViewerControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register(nameof(HeaderText), typeof(string), typeof(LogViewerControl),
            new PropertyMetadata("Logs"));

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(LogViewerControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string LogText
    {
        get => (string)GetValue(LogTextProperty);
        set => SetValue(LogTextProperty, value);
    }

    public event EventHandler? ClearRequested;

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ClearRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        CopyTextToClipboard(LogTextBox?.Text ?? LogText);
    }

    private void OnCopyCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var target = LogTextBox;
        if (target is null) return;
        var selected = string.IsNullOrEmpty(target.SelectedText) ? target.Text : target.SelectedText;
        CopyTextToClipboard(selected);
        e.Handled = true;
    }

    private void OnCopyCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !string.IsNullOrEmpty(LogTextBox?.Text);
        e.Handled = true;
    }

    private static void CopyTextToClipboard(string? text)
    {
        if (text is null) text = string.Empty;
        var normalized = NormalizeClipboardText(text);
        try
        {
            Clipboard.SetText(normalized, TextDataFormat.UnicodeText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Log copy failed: {ex}");
        }
    }

    private static string NormalizeClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var cleaned = text.Replace("\0", string.Empty);
        cleaned = cleaned.Replace("\r\n", "\n").Replace("\r", "\n");
        return cleaned.Replace("\n", Environment.NewLine);
    }
}
