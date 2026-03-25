# GUI Internals

## Crosspose.Gui (Main Dashboard)

### Window Structure

**MainWindow** — the primary application window (960x600):
- **Menu bar**: File (Quit), View (Log View), Tools (Doctor, Docker Desktop, Podman Desktop)
- **Sidebar** (220px): ListBox with Containers/Images/Volumes items
- **Content area**: Switches between TreeView (containers), ListView (images), ListView (volumes)

**LogWindow** — live log viewer:
- Subscribes to `InMemoryLogStore.OnWrite` event
- Loads existing `Snapshot()` on open, appends new lines in real-time
- Unsubscribes on close to prevent leaks

**ContainerDetailsWindow** — detail view for a container (900x600):
- Header: Name, Image, Status
- TabControl with placeholder tabs: Logs, Inspect, Bind mounts, Exec, Files, Stats
- All tabs show "coming soon" — not yet implemented

### View Models (defined in MainWindow.xaml.cs)

**ContainerRow** (`INotifyPropertyChanged`):
- `UniqueId`: composite `"platform:containerId"` used for start/stop routing
- `IsRunning` setter fires PropertyChanged for `IsRunning`, `ActionLabel`, and `State`
- `ActionLabel`: computed `"Stop"` / `"Start"`

**ProjectGroupRow**:
- Groups containers by `com.docker.compose.project` label
- `IsExpanded` persisted across refreshes (attempted, but current code clears Projects before reading expansion state — a bug)

**ImageRow**, **VolumeRow** (`INotifyPropertyChanged`):
- Simple property bags for images/volumes list views

### XAML Value Converters (defined in App.xaml.cs)

**StatusBrushConverter** (`IValueConverter`):
- `"running"` / `"available"` → green (#22C55E)
- `"paused"` → yellow (#EAB308)
- anything else → red (#EF4444)

**PlatformIconConverter** (`IValueConverter`):
- `"win*"` → `pack://application:,,,/logo_win.ico`
- anything else → `pack://application:,,,/logo_lin.ico`

### Container Data Flow

1. `MainWindow` constructor creates `DockerContainerRunner` + `PodmanContainerRunner` (with `runInsideWsl: false`), wraps in `CombinedContainerPlatformRunner`.
2. `OnLoaded` triggers initial refresh and starts `DispatcherTimer` (5s default, configurable via `GUI_REFRESH_INTERVAL` env var).
3. Each refresh:
   - Runs both `GetContainersDetailedAsync` (JSON parsed) and `GetContainersAsync` (raw text) in parallel
   - Groups containers by project label
   - Falls back to `ParseDockerTable` regex parsing if JSON missed entries
   - Marshals to UI thread via `Dispatcher.InvokeAsync`
   - Protects against concurrent refreshes with `_isRefreshing` flag

### External Process Launching

- **Doctor GUI**: Looks for `Crosspose.Doctor.Gui.exe` in `AppContext.BaseDirectory` first, falls back to PATH.
- **Docker Desktop**: Walks up from `docker.exe` location on PATH, searching parent directories for `Docker Desktop.exe`. Special handling for Docker's `resources` subdirectory layout.
- **Podman Desktop**: Launches `"podman-desktop"` via shell execute.

## Crosspose.Doctor.Gui

### Window Structure

**MainWindow**:
- `ListBox` (`ChecksList`) bound to `ObservableCollection<CheckViewModel>`
- Each item shows check name, result message, status, and a Fix button
- On load: runs all checks sequentially, updates view models inline

**FixWindow** (dialog):
- Shows real-time process output from `ProcessRunner.OutputHandler` in a scrolling TextBox
- Continue button disabled until fix completes
- Returns `DialogResult = true` on Continue click
- Caller reads `Success` and `FinalMessage` properties

### View Model

**CheckViewModel** (extends `DependencyObject`, not `INotifyPropertyChanged`):
- Uses WPF `DependencyProperty` for `Name`, `Result`, `IsSuccess`, `IsFixEnabled`
- Holds `ICheckFix Check` reference for fix execution

Note: Doctor.Gui uses `DependencyObject` while Gui uses `INotifyPropertyChanged` — different MVVM approaches in different projects.
