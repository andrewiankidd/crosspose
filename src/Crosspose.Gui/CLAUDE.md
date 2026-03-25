# CLAUDE.md — Crosspose.Gui

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Main WPF dashboard for Crosspose. Provides a container/image/volume management interface with sidebar navigation, launches Doctor.Gui and Dekompose.Gui as sub-tools.

## Sidebar Views

- **Setup**: Definitions (compose project definitions for deployment)
- **Runtime**: Projects, Containers, Images, Volumes

## Windows

- **MainWindow** — sidebar navigation, TreeView for containers grouped by project, ListView for images/volumes, auto-refresh via `DispatcherTimer`, start/stop/remove actions.
- **ContainerDetailsWindow** — container inspection with live logs tab (fetches via `docker/podman logs`).
- **LogWindow** — real-time log viewer subscribed to `InMemoryLogStore`.
- **AboutWindow** — version info.

## Tools Menu

Launches external processes:
- **Crosspose Doctor** — looks for `Crosspose.Doctor.Gui.exe` in output dir, falls back to PATH.
- **Crosspose Dekompose** — looks for `Crosspose.Dekompose.Gui.exe` similarly.
- **Docker Desktop** — PATH search walking up from `docker.exe` location.
- **Podman Desktop** — shell execute.

## Dependencies

- `Crosspose.Core` — container runners, logging, configuration.
- `Crosspose.Ui` — shared WPF controls.
- `Crosspose.Doctor.Gui` — output copied to bin dir (not assembly reference).
- `Crosspose.Dekompose.Gui` — output copied to bin dir (not assembly reference).
- `FluentIcons.Wpf` — icon set.
- Dark/light theme support via `Themes/Colors.Dark.xaml` and `Colors.Light.xaml`.
