# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this project.

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Unified CLI for container operations across Docker Desktop (Windows) and Podman (WSL). Will replace direct `docker`/`podman` usage for hybrid workload management.

## Build and Run

```powershell
dotnet run --project src/Crosspose.Cli -- ps -a
dotnet run --project src/Crosspose.Cli -- compose --action status
```

## Current State

- `ps` command works: creates `DockerContainerRunner` + `PodmanContainerRunner`, wraps them in `CombinedContainerPlatformRunner`, and prints a unified table.
- `compose` command is a stub — logs a message pointing to the PowerShell prototype at `C:\git\crossposeps\assets\scripts\compose.ps1`.
- Single-file `Program.cs` with top-level statements, manual arg parsing via `Queue<string>`.
- Uses `LaunchedOutsideShell()` guard to prevent double-click execution.

## Dependencies

- `Crosspose.Core` — for `ProcessRunner`, container runner abstractions, and logging.
