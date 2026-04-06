using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Crosspose.Gui;

public partial class PortableModeWindow : Window
{
    public List<DataItem> Items { get; } = [];

    private readonly string _exeDir;
    private readonly string _portableRoot;
    private readonly string _legacyRoaming;
    private readonly string _legacyHelm;

    public PortableModeWindow()
    {
        _exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _portableRoot = Path.Combine(_exeDir, "AppData", "crosspose");
        _legacyRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "crosspose");
        _legacyHelm = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Crosspose", "helm");

        BuildItems();
        DataContext = this;
        InitializeComponent();
    }

    private void BuildItems()
    {
        Items.Add(new DataItem
        {
            Name = "Configuration",
            Description = "crosspose.yml — your chart sources, Helm repos, and settings",
            SourcePath = Path.Combine(_legacyRoaming, "crosspose.yml"),
            DestPath = Path.Combine(_portableRoot, "crosspose.yml"),
            IsFile = true,
        });

        Items.Add(new DataItem
        {
            Name = "Deployments",
            Description = "crosspose-deployments\\ — generated compose files and deployment manifests",
            SourcePath = Path.Combine(_legacyRoaming, "crosspose-deployments"),
            DestPath = Path.Combine(_portableRoot, "crosspose-deployments"),
            IsFile = false,
        });

        Items.Add(new DataItem
        {
            Name = "Helm Cache",
            Description = "Pulled chart tarballs — can be re-downloaded if not migrated",
            SourcePath = _legacyHelm,
            DestPath = Path.Combine(_portableRoot, "helm"),
            IsFile = false,
        });

        Items.Add(new DataItem
        {
            Name = "Remaining app data",
            Description = "Everything else in %APPDATA%\\crosspose\\ (logs, sources, etc.)",
            SourcePath = _legacyRoaming,
            DestPath = _portableRoot,
            IsFile = false,
            IsCatchAll = true,
        });

        foreach (var item in Items)
            item.Refresh();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_portableRoot);

            // Move selected items, most specific first (config file, then named dirs, then catch-all)
            var ordered = Items
                .Where(i => i.ShouldMigrate && i.CanMigrate)
                .OrderBy(i => i.IsCatchAll)
                .ToList();

            foreach (var item in ordered)
            {
                try
                {
                    if (item.IsFile)
                    {
                        if (File.Exists(item.SourcePath) && !File.Exists(item.DestPath))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(item.DestPath)!);
                            File.Move(item.SourcePath, item.DestPath);
                        }
                    }
                    else if (item.IsCatchAll)
                    {
                        // Move remaining contents of source dir into dest dir (not already moved items)
                        if (Directory.Exists(item.SourcePath))
                            MergeDirectory(item.SourcePath, item.DestPath);
                    }
                    else
                    {
                        if (Directory.Exists(item.SourcePath) && !Directory.Exists(item.DestPath))
                            Directory.Move(item.SourcePath, item.DestPath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Could not move {item.Name}:\n{ex.Message}\n\nPortable mode will still be enabled — you can move this data manually.",
                        "Migration Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            // Create the .portable marker
            var portableMarker = Path.Combine(_exeDir, ".portable");
            File.WriteAllText(portableMarker, "");

            // Open the new data directory in Explorer
            Process.Start(new ProcessStartInfo
            {
                FileName = _portableRoot,
                UseShellExecute = true,
            });

            // Restart the application
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                });
            }

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to enable portable mode:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void MergeDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            if (!File.Exists(destFile))
                File.Move(file, destFile);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var destDir = Path.Combine(dest, Path.GetFileName(dir));
            if (!Directory.Exists(destDir))
                Directory.Move(dir, destDir);
            else
                MergeDirectory(dir, destDir);
        }
    }
}

public class DataItem : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string SourcePath { get; init; }
    public required string DestPath { get; init; }
    public bool IsFile { get; init; }
    public bool IsCatchAll { get; init; }

    private bool _canMigrate;
    private bool _shouldMigrate;

    public bool CanMigrate
    {
        get => _canMigrate;
        private set { _canMigrate = value; OnPropertyChanged(nameof(CanMigrate)); }
    }

    public bool ShouldMigrate
    {
        get => _shouldMigrate;
        set { _shouldMigrate = value; OnPropertyChanged(nameof(ShouldMigrate)); }
    }

    public void Refresh()
    {
        CanMigrate = IsFile ? File.Exists(SourcePath) : Directory.Exists(SourcePath);
        ShouldMigrate = CanMigrate;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
