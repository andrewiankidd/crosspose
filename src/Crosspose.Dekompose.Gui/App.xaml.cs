namespace Crosspose.Dekompose.Gui;

using System.Collections.Generic;
using System.Windows;

public partial class App : Application
{
    public static string? InitialChartPath { get; private set; }
    public static string? InitialValuesPath { get; private set; }
    public static string? InitialDekomposeConfigPath { get; private set; }
    public static bool AutoRun { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? output = null;
        string? chart = null;
        string? values = null;
        string? dekomposeConfig = null;
        bool compress = false;
        bool includeInfra = false;
        bool remapPorts = false;

        var queue = new Queue<string>(e.Args);
        while (queue.Count > 0)
        {
            var token = queue.Dequeue();
            switch (token)
            {
                case "--output":
                case "-o":
                    if (queue.Count > 0) output = queue.Dequeue();
                    break;
                case "--chart":
                case "-c":
                    if (queue.Count > 0) chart = queue.Dequeue();
                    break;
                case "--values":
                case "-v":
                    if (queue.Count > 0) values = queue.Dequeue();
                    break;
                case "--dekompose-config":
                    if (queue.Count > 0) dekomposeConfig = queue.Dequeue();
                    break;
                case "--compress":
                case "/compress":
                    compress = true;
                    break;
                case "--infra":
                case "/infra":
                case "--estimate-infra":
                    includeInfra = true;
                    break;
                case "--remap-ports":
                    remapPorts = true;
                    break;
                case "--auto-run":
                    AutoRun = true;
                    break;
            }
        }

        InitialChartPath = chart;
        InitialValuesPath = values;
        InitialDekomposeConfigPath = dekomposeConfig;

        var window = new MainWindow(output, compress, includeInfra, remapPorts);
        window.Show();
    }
}
