# CLAUDE.md — Crosspose.Gui

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Main WPF dashboard for Crosspose. Provides a container/image/volume management interface with sidebar navigation, launches Doctor.Gui and Dekompose.Gui as sub-tools.

## Sidebar Views

- **Setup**: Charts (browse/pull Helm chart tgz files), Compose Bundles (dekomposed zip bundles ready for deployment)
- **Runtime**: Projects, Containers, Images, Volumes

## Windows

- **MainWindow** — sidebar navigation, TreeView for containers grouped by project, ListView for images/volumes/charts, auto-refresh via `DispatcherTimer`, start/stop/remove actions, prune actions.
- **ContainerDetailsWindow** — container inspection with live logs tab (fetches via `docker/podman logs`).
- **PortableModeWindow** — guided dialog to enable portable mode (shows data items to migrate with source→dest paths).
- **LogWindow** — real-time log viewer subscribed to `InMemoryLogStore`.
- **AboutWindow** — version info.

## Menus

**Tools menu:**
- Crosspose Doctor — launches `Crosspose.Doctor.Gui.exe` (output dir, falls back to PATH).
- Crosspose Dekompose — launches `Crosspose.Dekompose.Gui.exe` similarly.
- Docker Desktop — PATH search walking up from `docker.exe` location.
- Podman Desktop — shell execute.
- Enable/Disable Offline Mode — toggles `DoctorSettings.IsOfflineMode`, persists to `crosspose.yml`.
- Enable Portable Mode — opens `PortableModeWindow` (only shown when not already portable).

**View menu:**
- Enable Dark Mode / Enable Light Mode — toggles theme at runtime, persists to `compose.gui.dark-mode` in `crosspose.yml`.

## Dependencies

- `Crosspose.Core` — container runners, logging, configuration.
- `Crosspose.Ui` — shared WPF components (`AddChartSourceWindow`, `PickChartWindow`, `LogViewerControl`).
- `Crosspose.Doctor.Gui` — output copied to bin dir (not assembly reference).
- `Crosspose.Dekompose.Gui` — output copied to bin dir (not assembly reference).
- `FluentIcons.Wpf` — icon set.
- Dark/light theme support via `Themes/Colors.Dark.xaml` and `Colors.Light.xaml`.
