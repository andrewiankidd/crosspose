using System.Text;
using System.Windows;
using System.Windows.Input;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Logging.Internal;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Gui;

public partial class FixAllWindow : Window
{
    private readonly IReadOnlyList<CheckViewModel> _fixable;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StringBuilder _buffer = new();

    public FixAllWindow(IReadOnlyList<CheckViewModel> fixable, ILoggerFactory loggerFactory)
    {
        InitializeComponent();
        _fixable = fixable;
        _loggerFactory = loggerFactory;
        Header.Text = $"Fixing {fixable.Count} item{(fixable.Count == 1 ? "" : "s")}...";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var succeeded = 0;
        var failed = new List<string>();

        for (var i = 0; i < _fixable.Count; i++)
        {
            var vm = _fixable[i];
            var check = vm.Check;

            AppendLine(new string('=', 60));
            AppendLine($"[{i + 1}/{_fixable.Count}] {check.Name}");
            AppendLine(new string('=', 60));
            AppendLine(check.Description);
            AppendLine("");

            var runner = new ProcessRunner(_loggerFactory.CreateLogger<ProcessRunner>())
            {
                OutputHandler = line => AppendLine(SecretCensor.Sanitize(line))
            };

            try
            {
                var result = await check.FixAsync(runner, _loggerFactory.CreateLogger(check.Name), default);
                AppendLine("");
                AppendLine(result.Message);

                if (result.Succeeded)
                {
                    AppendLine("Result: OK");
                    succeeded++;
                }
                else
                {
                    AppendLine("Result: FAILED");
                    failed.Add(check.Name);
                }
            }
            catch (Exception ex)
            {
                AppendLine($"Result: ERROR — {ex.Message}");
                failed.Add(check.Name);
            }

            AppendLine("");
        }

        AppendLine(new string('=', 60));
        AppendLine("SUMMARY");
        AppendLine(new string('=', 60));
        AppendLine($"Fixes attempted : {_fixable.Count}");
        AppendLine($"Succeeded       : {succeeded}");
        AppendLine($"Failed          : {failed.Count}");
        if (failed.Count > 0)
        {
            AppendLine($"Failed checks   : {string.Join(", ", failed)}");
        }

        Header.Text = failed.Count == 0
            ? $"All {succeeded} fix{(succeeded == 1 ? "" : "es")} completed successfully."
            : $"{succeeded} of {_fixable.Count} fix{(_fixable.Count == 1 ? "" : "es")} succeeded — {failed.Count} failed.";

        CloseButton.IsEnabled = true;
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

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(OutputBox.Text);
    }

    private void OnCopyCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        CopyToClipboard(string.IsNullOrEmpty(OutputBox?.SelectedText) ? OutputBox?.Text : OutputBox.SelectedText);
        e.Handled = true;
    }

    private void OnCopyCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !string.IsNullOrEmpty(OutputBox?.Text);
        e.Handled = true;
    }

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var cleaned = text.Replace("\0", string.Empty)
                          .Replace("\r\n", "\n")
                          .Replace("\r", "\n")
                          .Replace("\n", Environment.NewLine);
        try { Clipboard.SetText(cleaned, TextDataFormat.UnicodeText); }
        catch { /* best effort */ }
    }
}
