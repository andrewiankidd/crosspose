# Code Conventions

Patterns, naming, async usage, error handling, and known inconsistencies.

## Project Conventions

- **Top-level statements**: All CLI `Program.cs` files use C# top-level statements. Local functions are declared as `static` where possible.
- **Records for data**: Immutable data types use `record` (e.g., `ProcessResult`, `CheckResult`, `ContainerProcessInfo`).
- **Sealed by default**: All concrete non-abstract classes are `sealed`.
- **Nullable enabled**: All projects have `<Nullable>enable</Nullable>`. Null is used meaningfully (e.g., `Project` is null when a container has no compose project label).
- **No DI container in CLIs**: CLI projects manually construct their dependency graphs in `Program.cs`. Only `ILoggerFactory` is shared via `CrossposeLoggerFactory`.
- **No DI container in GUIs**: GUI projects also construct dependencies manually in constructors/`OnLoaded`.

## Async Patterns

- All process execution is async via `ProcessRunner.RunAsync`.
- GUI event handlers use `async void` (WPF requirement for event handlers).
- Parallel execution via `Task.WhenAll` for independent operations (e.g., docker + podman queries).
- `ConfigureAwait(false)` used in library code (`ContainerPlatformRunnerBase`, `CombinedContainerPlatformRunner`) but NOT in GUI code (which needs the dispatcher context).
- Cancellation supported throughout via `CancellationToken` parameters.

## Error Handling

- `ProcessRunner` catches `Win32Exception` with `NativeErrorCode == 2` to handle "command not found" — returns `ProcessResult(-1, "", "Command not found: ...")` instead of throwing.
- Doctor checks treat non-zero exit codes as check failures, not exceptions.
- Container runners swallow JSON parse exceptions and log warnings — partial results are better than crashes.
- `PodmanContainerRunner` has a `TableParseFallback` for when JSON parsing fails entirely.
- GUI container refresh retains previous data if a refresh returns 0 containers (likely a transient error).

## Naming Conventions

- **Projects**: `Crosspose.<Component>` (PascalCase).
- **Namespaces**: Match project names (e.g., `Crosspose.Dekompose.Services`).
- **Check names**: Lowercase with hyphens (`"docker-compose"`, `"wsl"`, `"crosspose-wsl-instance"`, `"helm"`).
- **Platform identifiers**: `"docker"`, `"podman"`, `"wsl-podman"`, `"combined"` for Platform field; `"win"`, `"lin"` for HostPlatform.
- **View model properties**: PascalCase, matching WPF binding conventions.

## Known Inconsistencies

See [tech-debt.md](tech-debt.md) for structural issues and [recommendations.md](recommendations.md) for the action plan. Key things to be aware of when writing code:

- **MVVM**: Doctor.Gui uses `DependencyObject`; Gui uses `INotifyPropertyChanged`.
- **Docker vs Podman output**: Different JSON structures and label formats — see tech-debt.md "Docker vs Podman output format differences" for details.

## Logging Format

Console logging uses `SimpleConsole` with:
- `SingleLine = true`
- `TimestampFormat = "HH:mm:ss "`
- `UseUtcTimestamp = false` (local time)

InMemoryLogStore formats as:
```
[HH:mm:ss] LogLevel    CategoryName: Message
```

## Gitignore Notes

- `docker-compose-outputs/` and `**/docker-compose-outputs/` are gitignored — these are generated artifacts from Dekompose.
- Standard .NET gitignore (bin/, obj/, .vs/, etc.).
- `.env` files are gitignored.
