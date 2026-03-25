# Architecture

## Solution Structure

```
Crosspose.sln
└── src/
    ├── Crosspose.Core/              # Class library — shared infrastructure
    ├── Crosspose.Cli/               # Console exe — unified container CLI
    ├── Crosspose.Dekompose/         # Class library — helm-to-compose conversion services
    ├── Crosspose.Dekompose.Cli/     # Console exe — Dekompose CLI entry point
    ├── Crosspose.Doctor/            # Class library — prerequisite checks
    ├── Crosspose.Doctor.Cli/        # Console exe — Doctor CLI entry point
    ├── Crosspose.Doctor.Gui/        # WPF exe — Doctor UI
    └── Crosspose.Gui/               # WPF exe — main dashboard
```

## Naming Convention

Libraries hold logic, `.Cli`/`.Gui` projects are thin entry points:
- `Crosspose.X` — library with reusable logic
- `Crosspose.X.Cli` — CLI entry point (Program.cs + arg parsing)
- `Crosspose.X.Gui` — GUI entry point (WPF window)

## Dependency Graph

```
Crosspose.Core  (no project references)
    ↑
    ├── Crosspose.Cli
    ├── Crosspose.Dekompose
    │       ↑
    │       └── Crosspose.Dekompose.Cli
    ├── Crosspose.Doctor
    │       ↑
    │       ├── Crosspose.Doctor.Cli
    │       └── Crosspose.Doctor.Gui
    └── Crosspose.Gui ──→ Crosspose.Doctor.Gui (output copy only, not assembly reference)
```

All libraries depend on Core. CLI/GUI projects depend on their library + Core. Gui copies Doctor.Gui's build output into its own bin directory via MSBuild Content items — it launches Doctor.Gui as a separate process.

## Namespace Map

| Project | Root Namespace |
|---------|---------------|
| Crosspose.Core | `Crosspose.Core.Diagnostics`, `Crosspose.Core.Orchestration`, `Crosspose.Core.Logging` |
| Crosspose.Cli | Top-level statements (no namespace) |
| Crosspose.Dekompose | `Crosspose.Dekompose.Services` |
| Crosspose.Dekompose.Cli | Top-level statements (no namespace) |
| Crosspose.Doctor | `Crosspose.Doctor`, `Crosspose.Doctor.Checks` |
| Crosspose.Doctor.Cli | Top-level statements (no namespace) |
| Crosspose.Doctor.Gui | `Crosspose.Doctor.Gui` |
| Crosspose.Gui | `Crosspose.Gui` |

## Target Frameworks

| Project | TFM | UI Framework |
|---------|-----|-------------|
| Core | `net10.0` | None |
| Cli | `net10.0` | None |
| Dekompose | `net10.0` | None |
| Dekompose.Cli | `net10.0` | None |
| Doctor | `net10.0` | None |
| Doctor.Cli | `net10.0` | None |
| Doctor.Gui | `net10.0-windows10.0.19041` | WPF |
| Gui | `net10.0-windows10.0.19041` | WPF |

## Core Internal Structure

```
Crosspose.Core/
├── Diagnostics/
│   ├── ProcessRunner.cs        # Async process execution with stdout/stderr capture
│   ├── ProcessResult.cs        # Immutable result record
│   └── ShellDetection.cs       # Heuristic to detect double-click launches
├── Orchestration/
│   ├── IVirtualizationPlatformRunner.cs    # Base interface for any CLI wrapper
│   ├── VirtualizationPlatformRunnerBase.cs # Base implementation
│   ├── IContainerPlatformRunner.cs         # Container-specific operations
│   ├── ContainerPlatformRunnerBase.cs      # Default container operations
│   ├── DockerContainerRunner.cs            # Docker CLI wrapper with JSON parsing
│   ├── PodmanContainerRunner.cs            # Podman CLI wrapper (optional WSL prefix)
│   ├── CombinedContainerPlatformRunner.cs  # Aggregates docker + podman
│   ├── WslRunner.cs                        # WSL CLI wrapper
│   ├── IContainerProcess.cs                # Data records: ContainerProcessInfo, ImageInfo, VolumeInfo
│   └── JsonExtensions.cs                   # Safe JsonElement property access
└── Logging/
    ├── CrossposeLoggerFactory.cs           # ILoggerFactory builder
    └── Internal/
        └── InMemoryLogProvider.cs          # InMemoryLogStore + ILoggerProvider for GUI log windows
```

## Inheritance Hierarchy

```
IVirtualizationPlatformRunner
└── VirtualizationPlatformRunnerBase
    ├── WslRunner
    └── ContainerPlatformRunnerBase (implements IContainerPlatformRunner)
        ├── DockerContainerRunner
        └── PodmanContainerRunner

IContainerPlatformRunner
└── CombinedContainerPlatformRunner (composes two IContainerPlatformRunner instances)
```

Note: `CombinedContainerPlatformRunner` implements `IContainerPlatformRunner` but does NOT extend `ContainerPlatformRunnerBase` — it's a composite that delegates to docker + podman runners.
