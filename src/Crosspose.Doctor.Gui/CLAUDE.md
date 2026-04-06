# CLAUDE.md — Crosspose.Doctor.Gui

See also: [root CLAUDE.md](../../CLAUDE.md) | [Doctor library](../Crosspose.Doctor/CLAUDE.md)

## Purpose

WPF GUI for Doctor. Lists each prerequisite check with status, description, and a per-item Fix button. Re-verifies after fix completes.

## Windows

- **MainWindow** — check list with auto-run on load, Fix buttons, Fix All button, About/Quit menu, double-click for details. Shows amber offline mode banner when offline mode is active.
- **FixWindow** — dialog showing real-time fix output for a single check, Copy button for output text.
- **FixAllWindow** — dialog that runs fixes for all failed fixable checks in sequence, with streaming output.
- **AboutWindow** — version info.

## Architecture

- `CheckViewModel` uses `DependencyProperty` (not `INotifyPropertyChanged`).
- Loads checks via `CheckCatalog.LoadAll(offlineMode: settings.OfflineMode)` with settings from `DoctorSettings.Load()`.
- When offline mode is active, an amber banner is shown and connectivity-requiring checks are omitted.
- Dark/light theme support via `Themes/Colors.Dark.xaml` and `Colors.Light.xaml`.

## Dependencies

- `Crosspose.Core`, `Crosspose.Doctor`
