using System.Windows;
using Crosspose.Core.Configuration;

namespace Crosspose.Doctor.Gui;

public partial class App : Application
{
    /// <summary>
    /// When true, Doctor runs all fixes silently then closes — used when launched by
    /// Crosspose.Gui after 'up' to auto-configure portproxy rules without user interaction.
    /// </summary>
    public static bool AutoFixMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!AppDataLocator.IsRunningElevated)
        {
            MessageBox.Show(
                "Crosspose Doctor requires Administrator privileges to apply fixes " +
                "(port proxies, firewall rules, service restarts).\n\n" +
                "Please relaunch from an elevated terminal.",
                "Crosspose Doctor — Administrator required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        foreach (var arg in e.Args)
        {
            if (arg.Equals("--auto-fix", StringComparison.OrdinalIgnoreCase))
            {
                AutoFixMode = true;
            }
        }
    }
}

