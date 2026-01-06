using System.Text;
using System.Windows;
using System.Windows.Input;
using Crosspose.Core.Diagnostics;
using Crosspose.Doctor.Checks;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Gui;

public partial class FixWindow : Window
{
    private readonly ICheckFix _check;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StringBuilder _buffer = new();

    public string FinalMessage { get; private set; } = string.Empty;
    public bool Success { get; private set; }

    public FixWindow(ICheckFix check, ILoggerFactory loggerFactory)
    {
        InitializeComponent();
        _check = check;
        _loggerFactory = loggerFactory;
        Header.Text = $"Running fix: {check.Name}";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var runner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>())
        {
            OutputHandler = line => AppendLine(line)
        };

        AppendLine("Starting fix...");
        var result = await _check.FixAsync(runner, _loggerFactory.CreateLogger(_check.Name), default);
        Success = result.Succeeded;
        FinalMessage = result.Message;

        AppendLine("");
        AppendLine(result.Message);
        AppendLine(Success ? "Fix completed successfully." : "Fix completed with errors.");

        ContinueButton.IsEnabled = true;
    }

    private void AppendLine(string line)
    {
        Dispatcher.Invoke(() =>
        {
            _buffer.AppendLine(line);
            OutputBox.Text = _buffer.ToString();
            OutputBox.ScrollToEnd();
        });
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        CopyOutputToClipboard(OutputBox.Text);
    }

    private void OnCopyCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var text = OutputBox?.SelectedText;
        if (string.IsNullOrEmpty(text))
        {
            text = OutputBox?.Text;
        }
        CopyOutputToClipboard(text);
        e.Handled = true;
    }

    private void OnCopyCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !string.IsNullOrEmpty(OutputBox?.Text);
        e.Handled = true;
    }

    private static void CopyOutputToClipboard(string? text)
    {
        var normalized = NormalizeClipboardText(text);
        try
        {
            Clipboard.SetText(normalized, TextDataFormat.UnicodeText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fix output copy failed: {ex}");
        }
    }

    private static string NormalizeClipboardText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var cleaned = text.Replace("\0", string.Empty);
        cleaned = cleaned.Replace("\r\n", "\n").Replace("\r", "\n");
        return cleaned.Replace("\n", Environment.NewLine);
    }
}
