namespace Crosspose.Dekompose.Gui;

using System.Collections.Generic;
using System.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? output = null;
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
            }
        }

        var window = new MainWindow(output, compress, includeInfra, remapPorts);
        window.Show();
    }
}
