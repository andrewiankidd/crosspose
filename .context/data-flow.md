# Data Flow

How data moves through the system for each major operation.

## 1. Container Enumeration (CLI `ps` and GUI Containers view)

```
User invokes `crosspose ps -a` or GUI loads
    │
    ▼
CombinedContainerPlatformRunner
    ├── DockerContainerRunner.GetContainersDetailedAsync()
    │       │
    │       ▼
    │   ProcessRunner.RunAsync("docker", "ps -a --no-trunc --format json")
    │       │
    │       ▼
    │   Parse JSON output (array or newline-delimited objects)
    │   Extract com.docker.compose.project from Labels string
    │   Set HostPlatform = "win"
    │       │
    │       ▼
    │   List<ContainerProcessInfo>
    │
    ├── PodmanContainerRunner.GetContainersDetailedAsync()
    │       │
    │       ▼
    │   ProcessRunner.RunAsync("podman", "ps -a --format json")
    │   (or "wsl", "podman ps -a --format json" if runInsideWsl=true)
    │       │
    │       ▼
    │   Parse JSON array
    │   Extract com.docker.compose.project from Labels object
    │   Set HostPlatform = "lin"
    │   Falls back to regex table parsing if JSON parse fails
    │       │
    │       ▼
    │   List<ContainerProcessInfo>
    │
    ▼
Concat docker + podman results
    │
    ▼
CLI: Print formatted table (OS, Platform, Container, Image, Status)
GUI: Group by Project → ProjectGroupRow → ContainerRow, bind to TreeView
```

### GUI Refresh Loop
- `DispatcherTimer` fires every 5 seconds (configurable via `GUI_REFRESH_INTERVAL` env var).
- Calls `RefreshCurrentViewAsync()` which dispatches to `ShowContainersAsync`, `ShowImagesAsync`, or `ShowVolumesAsync` based on sidebar selection.
- Uses `_isRefreshing` flag + `_pendingRefresh` to avoid concurrent refreshes and queue at most one pending refresh.
- 15-second `CancellationTokenSource` timeout on container refresh.
- If parsed 0 containers but UI has existing entries, retains previous list to avoid flicker.
- Also parses raw `CombinedContainerPlatformRunner.GetContainersAsync` text output as fallback, using `ParseDockerTable` to extract containers from the `=== containers (docker) ===` section.

### Container Start/Stop (GUI)
```
User clicks Start/Stop button on ContainerRow
    │
    ▼
CombinedContainerPlatformRunner.StartContainerAsync("docker:containerId")
    │
    ▼
Parse "platform:containerId" composite ID
Route to docker.StartContainerAsync(containerId) or podman.StartContainerAsync(containerId)
    │
    ▼
ProcessRunner.RunAsync("docker", "start containerId")
    │
    ▼
Update ContainerRow.IsRunning → triggers INotifyPropertyChanged → UI updates ActionLabel
```

## 2. Helm Template Rendering (Dekompose)

```
User invokes: dekompose --chart C:\path\to\chart --values C:\values.yaml
    │
    ▼
ParseArgs() → DekomposeOptions
    │
    ▼
HelmTemplateRunner.RenderAsync(chartPath, valuesPath, outputDirectory)
    │
    ▼
ProcessRunner.RunAsync("helm", "template crosspose \"chartPath\" --values \"valuesPath\"", workingDirectory: chartPath)
    │
    ▼
Write stdout to rendered.<timestamp>.yaml in output directory
    │
    ▼
ComposeStubWriter.WritePlaceholderAsync(manifestPath, outputDirectory)
    │
    ▼
Copy manifest to rendered.manifest.yaml
Write TODO.compose-generation.md with porting instructions
```

If `--manifest` is provided instead of `--chart`, helm rendering is skipped and the manifest is used directly.

## 3. Doctor Prerequisite Checks

```
User invokes: doctor [--fix]
    │
    ▼
CheckCatalog.LoadAll() → [DockerComposeCheck, WslCheck, CrossposeWslCheck, HelmCheck]
    │
    ▼
For each check:
    │
    ├── check.RunAsync(runner, logger, ct)
    │       │
    │       ▼
    │   ProcessRunner.RunAsync(<tool>, <version/status args>)
    │       │
    │       ▼
    │   CheckResult(IsSuccessful, Message)
    │
    ├── If failed AND --fix AND check.CanFix:
    │       │
    │       ▼
    │   check.FixAsync(runner, logger, ct)
    │       │
    │       ▼
    │   ProcessRunner.RunAsync("winget", "install ...") or ProcessRunner.RunAsync("wsl", "--install ...")
    │       │
    │       ▼
    │   FixResult(Succeeded, Message)
    │
    ▼
Exit code: 0 if all pass, 1 if any failures remain
```

### Doctor.Gui Flow
```
MainWindow.OnLoaded
    │
    ▼
CheckCatalog.LoadAll()
For each check:
    RunAsync → update CheckViewModel (Result, IsSuccess, IsFixEnabled)
    │
    ▼
User clicks Fix button on failed check
    │
    ▼
Open FixWindow dialog
    FixWindow.OnLoaded → check.FixAsync with OutputHandler piping to TextBox
    User clicks Continue → DialogResult = true
    │
    ▼
MainWindow updates CheckViewModel with fix result
```

## 4. Logging Flow

```
ProcessRunner (or any ILogger consumer)
    │
    ▼
ILoggerFactory (from CrossposeLoggerFactory.Create)
    ├── SimpleConsoleLogger → stdout
    └── InMemoryLogProvider (if logStore provided)
            │
            ▼
        InMemoryLogStore.Write(formatted line)
            ├── Enqueue to ConcurrentQueue (cap 1000)
            └── Fire OnWrite event
                    │
                    ▼
                LogWindow.HandleWrite → Dispatcher.Invoke → append to TextBox

Additionally:
ProcessRunner.OutputHandler → InMemoryLogStore.Write (for real-time process output in GUI)
```
