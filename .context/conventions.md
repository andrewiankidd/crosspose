# Code Conventions

## Project Conventions

- **Library + entry point split**: Libraries hold logic (`Crosspose.X`), CLIs/GUIs are thin wrappers (`Crosspose.X.Cli`/`.Gui`).
- **Top-level statements**: All CLI `Program.cs` files use C# top-level statements.
- **Records for data**: Immutable data types use `record` (`ProcessResult`, `CheckResult`, `ContainerProcessInfo`, `ComposeExecutionRequest`, etc.).
- **Sealed by default**: All concrete non-abstract classes are `sealed`.
- **Nullable enabled**: All projects. Null is used meaningfully (e.g., `Project` is null for non-compose containers).
- **Manual DI**: No DI container — dependency graphs constructed manually in Program.cs / window constructors.
- **Configuration centralized**: Everything reads from `crosspose.yml` via `CrossposeConfigurationStore` / `CrossposeEnvironment`.
- **Portable mode**: `.portable` file beside exe switches all data to `.\AppData\crosspose\`.

## Async Patterns

- All process execution is async via `ProcessRunner.RunAsync`.
- GUI event handlers use `async void` (WPF requirement).
- Parallel execution via `Task.WhenAll` for docker + podman queries.
- `ConfigureAwait(false)` in library code, not in GUI code.
- Cancellation supported throughout via `CancellationToken`.

## Error Handling

- `ProcessRunner` catches `Win32Exception` (NativeErrorCode 2) for "command not found" — returns `ProcessResult(-1, "", "Command not found: ...")`.
- Doctor checks treat non-zero exit codes as failures, not exceptions.
- Container runners swallow JSON parse exceptions and log warnings — partial results preferred over crashes.
- `PodmanContainerRunner` has `TableParseFallback` for non-JSON output.
- GUI retains previous data if a refresh returns 0 containers (transient error protection).

## Naming

- **Projects**: `Crosspose.<Component>` (PascalCase).
- **Namespaces**: Match project names (`Crosspose.Dekompose.Services`, `Crosspose.Doctor.Checks`).
- **Check names**: Lowercase with hyphens (`"docker-compose"`, `"crosspose-wsl-instance"`).
- **Platform identifiers**: `"docker"`, `"podman"`, `"wsl-podman"` for Platform; `"win"`, `"lin"` for HostPlatform.
- **Compose actions**: `ComposeAction` enum: `Up`, `Down`, `Restart`, `Stop`, `Start`, `Logs`, `Top`, `Ps`.

## Logging

- All sinks (console, file, in-memory) sanitize JWTs and bearer tokens via `SecretCensor`.
- Serilog file sink with daily rolling, 14-day retention at `%APPDATA%\crosspose\logs\crosspose.log`.
- Console uses `SanitizingConsoleLoggerProvider` (not the default `SimpleConsole`).
- In-memory store is a thread-safe `ConcurrentQueue` (1000-line cap) with `OnWrite` event.

## Theme Support

GUI projects have `Themes/Colors.Dark.xaml` and `Themes/Colors.Light.xaml` resource dictionaries. Theme is applied at app startup via `App.xaml` merged dictionaries.
