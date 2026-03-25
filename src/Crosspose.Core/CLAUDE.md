# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this project.

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Shared class library that every other Crosspose project depends on. Provides process execution, container runtime abstractions, and logging infrastructure.

## Build

```powershell
dotnet build src/Crosspose.Core/Crosspose.Core.csproj
```

## Key Components

### Diagnostics
- `ProcessRunner` — the single entry point for executing external tools (helm, docker, podman, wsl, winget). Wraps `System.Diagnostics.Process` with async stdout/stderr capture, cancellation support, and automatic `Win32Exception` handling for missing commands. Has an optional `OutputHandler` callback for real-time line output.
- `ProcessResult` — immutable record: `ExitCode`, `StandardOutput`, `StandardError`, `IsSuccess`.

### Orchestration
Layered abstraction for container runtimes:
- `IVirtualizationPlatformRunner` → `VirtualizationPlatformRunnerBase` — base interface/class for any CLI wrapper. Holds `BaseCommand` and delegates to `ProcessRunner`.
- `IContainerPlatformRunner` → `ContainerPlatformRunnerBase` — extends with container operations (`GetContainersAsync`, `GetContainersDetailedAsync`). Parses JSON from `docker/podman ps --format json` and `inspect`.
- `DockerContainerRunner` — wraps `docker` CLI.
- `PodmanContainerRunner` — wraps `podman` CLI, with a `runInsideWsl` flag to optionally prefix commands with `wsl`.
- `CombinedContainerPlatformRunner` — aggregates Docker + Podman results into a single list. Used by both the CLI `ps` command and the GUI container view.
- `WslRunner` — wraps `wsl` CLI for running commands inside WSL distributions.
- `JsonExtensions` — helper for safe `JsonElement` property access.

### Logging
- `CrossposeLoggerFactory.Create()` — builds an `ILoggerFactory` with console logging. Optionally accepts an `InMemoryLogStore` for GUI log windows.
- `InMemoryLogStore` — thread-safe bounded queue (1000 lines) with `OnWrite` event. Used by WPF apps to display live log output.
