# CLAUDE.md — Crosspose.Doctor.Gui

See also: [root CLAUDE.md](../../CLAUDE.md) | [Doctor library](../Crosspose.Doctor/CLAUDE.md)

## Purpose

WPF GUI for Doctor. Lists each prerequisite check with status, description, and a per-item Fix button. Re-verifies after fix completes.

## Windows

- **MainWindow** — check list with auto-run on load, Fix buttons, About/Quit menu, double-click for details.
- **FixWindow** — dialog showing real-time fix output, Copy button for output text.
- **AboutWindow** — version info.

## Architecture

- `CheckViewModel` uses `DependencyProperty` (not `INotifyPropertyChanged`).
- Loads checks via `CheckCatalog.LoadAll()` with settings from `DoctorSettings.Load()`.
- Dark/light theme support via `Themes/Colors.Dark.xaml` and `Colors.Light.xaml`.

## Dependencies

- `Crosspose.Core`, `Crosspose.Doctor`
