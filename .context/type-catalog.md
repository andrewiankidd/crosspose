# Type Catalog

Every public/internal type with signatures, purpose, and usage context.

## Crosspose.Core.Diagnostics

### `ProcessRunner` (sealed class)
```csharp
public ProcessRunner(ILogger logger)
public Action<string>? OutputHandler { get; set; }
public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken cancellationToken = default)
```
- The single entry point for all external process execution in the codebase.
- `OutputHandler` is set by GUI code to route real-time process output to `InMemoryLogStore`.
- Handles `Win32Exception` with `NativeErrorCode == 2` (command not found) gracefully — returns `ProcessResult(-1, "", "Command not found: ...")`.
- Has `internal` log helper methods (`LogInformation`, `LogDebug`, `LogWarning`) used by container runners that hold a reference to the `Runner` field.

### `ProcessResult` (sealed record)
```csharp
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
public bool IsSuccess => ExitCode == 0;
```

## Crosspose.Core.Orchestration

### `IVirtualizationPlatformRunner` (interface)
```csharp
string BaseCommand { get; }
Task<ProcessResult> ExecAsync(IEnumerable<string> args, CancellationToken cancellationToken = default)
```

### `PlatformCommandResult` (record)
```csharp
public record PlatformCommandResult(string Platform, ProcessResult Result)
public bool HasError => !Result.IsSuccess;
public string Error => Result.StandardError;
```

### `VirtualizationPlatformRunnerBase` (abstract class)
```csharp
protected VirtualizationPlatformRunnerBase(string baseCommand, ProcessRunner runner)
```
- Joins `args` with spaces and delegates to `ProcessRunner.RunAsync`.
- Exposes `Runner` as `protected readonly` — subclasses use it for logging and direct process calls.

### `IContainerPlatformRunner` (interface, extends `IVirtualizationPlatformRunner`)
```csharp
Task<PlatformCommandResult> GetContainersAsync(bool includeAll = true, ...)
Task<PlatformCommandResult> GetImagesAsync(...)
Task<PlatformCommandResult> GetVolumesAsync(...)
Task<IReadOnlyList<ContainerProcessInfo>> GetContainersDetailedAsync(bool includeAll = true, ...)
Task<IReadOnlyList<ImageInfo>> GetImagesDetailedAsync(...)
Task<IReadOnlyList<VolumeInfo>> GetVolumesDetailedAsync(...)
Task<bool> StartContainerAsync(string id, ...)
Task<bool> StopContainerAsync(string id, ...)
```

### `ContainerPlatformRunnerBase` (abstract class)
- Default implementations for all `IContainerPlatformRunner` methods.
- `GetContainersAsync`/`GetImagesAsync`/`GetVolumesAsync` execute CLI commands and wrap results.
- `GetContainersDetailedAsync`/`GetImagesDetailedAsync`/`GetVolumesDetailedAsync` return empty arrays by default — overridden in Docker/Podman runners.
- `StartContainerAsync`/`StopContainerAsync` execute `start`/`stop` subcommands.

### `DockerContainerRunner` (sealed class)
- Base command: `"docker"`.
- Parses JSON output from `docker ps --no-trunc --format json`.
- Docker outputs either a JSON array OR newline-delimited JSON objects — `EnumerateJsonElements` handles both formats.
- Extracts `com.docker.compose.project` from the comma-separated `Labels` string field.
- Sets `HostPlatform = "win"` for all containers.

### `PodmanContainerRunner` (sealed class)
```csharp
public PodmanContainerRunner(ProcessRunner runner, bool runInsideWsl = false)
```
- When `runInsideWsl = true`: base command is `"wsl"`, and `"podman"` is prepended to all args.
- When `runInsideWsl = false`: base command is `"podman"` directly.
- Parses JSON array output from `podman ps --format json`.
- Podman's `Labels` field is a JSON object (not a comma-separated string like Docker), so parsing differs.
- Has `TableParseFallback` that regex-parses tabular output if JSON parsing fails.
- Sets `HostPlatform = "lin"` for all containers.

### `CombinedContainerPlatformRunner` (sealed class)
- Composes a docker + podman runner.
- All operations run both platforms in parallel via `Task.WhenAll`, then merge results.
- `ExecAsync` throws `NotSupportedException` — callers must use specific methods.
- `StartContainerAsync`/`StopContainerAsync` route by parsing a `"platform:containerId"` composite ID format.
- `Merge` helper produces formatted text with `=== containers (docker) ===` / `=== containers (podman) ===` section headers.

### `WslRunner` (sealed class)
- Base command: `"wsl"`. No additional logic beyond the base class.

### `IContainerProcess` / `ContainerProcessInfo` (interface + sealed record)
```csharp
public sealed record ContainerProcessInfo(
    string Platform, string Id, string Name, string Image,
    string Status, string State, string Ports, string? Project, string HostPlatform)
public bool IsRunning => string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);
```
- `Platform`: `"docker"`, `"podman"`, or `"wsl-podman"`.
- `HostPlatform`: `"win"` or `"lin"`.
- `Project`: extracted from `com.docker.compose.project` label; null if not a compose container.

### `ImageInfo` (sealed record)
```csharp
public sealed record ImageInfo(string Platform, string Name, string Tag, string Id, string Size, string HostPlatform)
```

### `VolumeInfo` (sealed record)
```csharp
public sealed record VolumeInfo(string Platform, string Name, string Size, string HostPlatform)
```

### `JsonExtensions` (internal static class)
```csharp
internal static string GetPropertyOrDefault(this JsonElement element, string propertyName, string defaultValue = "")
```

## Crosspose.Core.Logging

### `CrossposeLoggerFactory` (static class)
```csharp
public static ILoggerFactory Create(LogLevel minimumLogLevel = LogLevel.Information, InMemoryLogStore? logStore = null)
```
- Configures `SimpleConsole` with single-line format, `HH:mm:ss` timestamps, local time.
- When `logStore` is provided, adds an `InMemoryLogProvider` that writes to the store.

### `InMemoryLogStore` (sealed class)
```csharp
public event Action<string>? OnWrite;
public void Write(string line)
public IReadOnlyList<string> Snapshot()
```
- Thread-safe `ConcurrentQueue` with 1000-line cap.
- `OnWrite` event fires synchronously on the writing thread — GUI subscribers must `Dispatcher.Invoke` to marshal to UI thread.

## Crosspose.Doctor.Checks

### `ICheckFix` (interface)
```csharp
string Name { get; }
bool CanFix { get; }
Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
```

### `CheckResult` / `FixResult` (sealed records)
```csharp
public sealed record CheckResult(bool IsSuccessful, string Message)
public sealed record FixResult(bool Succeeded, string Message)
```
Both have static `Success(message)` and `Failure(message)` factory methods.

### `DockerComposeCheck`, `WslCheck`, `HelmCheck`, `CrossposeWslCheck`
All implement `ICheckFix`. See [external-tools.md](external-tools.md) for exact commands each check runs.

### `CheckCatalog` (static class)
```csharp
public static IReadOnlyList<ICheckFix> LoadAll()
```
Returns: `[DockerComposeCheck, WslCheck, CrossposeWslCheck, HelmCheck]` — order matters for display.

## Dekompose.Services

### `HelmTemplateRunner` (sealed class)
```csharp
public HelmTemplateRunner(ProcessRunner processRunner, ILogger<HelmTemplateRunner> logger)
public Task<HelmRenderResult> RenderAsync(string chartPath, string? valuesPath, string outputDirectory, CancellationToken cancellationToken)
```

### `HelmRenderResult` (sealed record)
```csharp
public sealed record HelmRenderResult(bool Succeeded, string RenderedManifestPath, bool UsedHelm, string? Error)
```

### `ComposeStubWriter` (sealed class)
```csharp
public ComposeStubWriter(ILogger<ComposeStubWriter> logger)
public Task WritePlaceholderAsync(string manifestPath, string outputDirectory, CancellationToken cancellationToken)
```

## Crosspose.Gui View Models

Defined in `MainWindow.xaml.cs` (not a separate file):

### `ContainerRow` (class, implements `INotifyPropertyChanged`)
- Properties: `UniqueId`, `Platform`, `HostPlatform`, `Id`, `Image`, `Ports`, `State`, `Status`, `Project`, `IsRunning`, `IsSelected`.
- `UniqueId` format: `"platform:containerId"` (e.g., `"docker:abc123"`).
- `ActionLabel` computed property: `"Stop"` when running, `"Start"` when stopped.

### `ProjectGroupRow` (class)
- Groups containers by their `com.docker.compose.project` label.
- `Containers`: `ObservableCollection<ContainerRow>`.

### `ImageRow`, `VolumeRow` (classes, implement `INotifyPropertyChanged`)

## Crosspose.Doctor.Gui View Models

### `CheckViewModel` (class, extends `DependencyObject`)
- WPF dependency properties: `Name`, `Result`, `IsSuccess`, `IsFixEnabled`.
- Holds reference to `ICheckFix Check` for fix execution.
