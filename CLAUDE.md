# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Crosspose?

Crosspose is a Windows-first PoC dev tool that converts Helm/Kubernetes charts into Docker Compose stacks, then orchestrates Windows containers (Docker Desktop) and Linux containers (Podman in WSL) side-by-side. This is the .NET rewrite of a PowerShell prototype at `C:\git\crossposeps`.

## Build and Run

```powershell
# Build the entire solution
dotnet build Crosspose.sln

# Run individual projects
dotnet run --project src/Crosspose.Gui                # Main WPF GUI
dotnet run --project src/Crosspose.Doctor.Cli          # Prerequisite checker CLI
dotnet run --project src/Crosspose.Doctor.Cli -- --fix # Auto-fix prerequisites
dotnet run --project src/Crosspose.Dekompose.Cli -- --chart <path> --values <values.yaml>
dotnet run --project src/Crosspose.Cli -- ps -a        # List containers across docker+podman
```

There are no tests yet. The roadmap includes adding tests for the conversion pipeline and docker/podman command brokering.

## Tech Stack

- .NET 10 (`net10.0`), C# with nullable reference types and implicit usings
- WPF (`net10.0-windows10.0.19041`) for GUI projects (Doctor.Gui and Gui)
- No third-party NuGet packages beyond `Microsoft.Extensions.*` (Logging, DI, Configuration)
- CLI projects use top-level statements for Program.cs entry points

## Architecture

Eight projects in `src/`, all under a single `Crosspose.sln`. Pattern: libraries hold logic, `.Cli`/`.Gui` projects are thin entry points.

| Project | Type | CLAUDE.md |
|---------|------|-----------|
| **Crosspose.Core** | Class library | [src/Crosspose.Core/CLAUDE.md](src/Crosspose.Core/CLAUDE.md) |
| **Crosspose.Cli** | CLI exe | [src/Crosspose.Cli/CLAUDE.md](src/Crosspose.Cli/CLAUDE.md) |
| **Crosspose.Dekompose** | Class library | [src/Crosspose.Dekompose/CLAUDE.md](src/Crosspose.Dekompose/CLAUDE.md) |
| **Crosspose.Dekompose.Cli** | CLI exe | [src/Crosspose.Dekompose.Cli/CLAUDE.md](src/Crosspose.Dekompose.Cli/CLAUDE.md) |
| **Crosspose.Doctor** | Class library | [src/Crosspose.Doctor/CLAUDE.md](src/Crosspose.Doctor/CLAUDE.md) |
| **Crosspose.Doctor.Cli** | CLI exe | [src/Crosspose.Doctor.Cli/CLAUDE.md](src/Crosspose.Doctor.Cli/CLAUDE.md) |
| **Crosspose.Doctor.Gui** | WPF exe | [src/Crosspose.Doctor.Gui/CLAUDE.md](src/Crosspose.Doctor.Gui/CLAUDE.md) |
| **Crosspose.Gui** | WPF exe | [src/Crosspose.Gui/CLAUDE.md](src/Crosspose.Gui/CLAUDE.md) |

**Crosspose.Core** — shared infrastructure: `ProcessRunner` (async process execution), container runtime abstractions (`DockerContainerRunner`, `PodmanContainerRunner`, `CombinedContainerPlatformRunner`, `WslRunner`), and logging (`CrossposeLoggerFactory` + `InMemoryLogStore`).

**Crosspose.Dekompose** — Helm-to-Compose conversion library. `HelmTemplateRunner`, `ComposeStubWriter`, and future compose generation services.

**Crosspose.Dekompose.Cli** — thin CLI entry point for Dekompose. Parses args, invokes services.

**Crosspose.Cli** — unified CLI for container operations. `ps` aggregates docker+podman containers. `compose` is a stub pending port from `crossposeps`.

**Crosspose.Doctor** — prerequisite check library. `ICheckFix` interface, `CheckCatalog`, checks: DockerCompose, WSL, CrossposeWsl, Helm.

**Crosspose.Doctor.Cli** — thin CLI entry point for Doctor. `--fix` triggers winget/wsl remediation.

**Crosspose.Doctor.Gui** — WPF front-end for Doctor. Reuses `CheckCatalog` and `ICheckFix` from the Doctor library.

**Crosspose.Gui** — main WPF dashboard. Container/Image/Volume views, log window, launches Doctor.Gui via Tools menu.

## Key Patterns

- All external tool execution goes through `ProcessRunner.RunAsync()` — never call `Process.Start()` directly.
- CLI entry points use `ShellDetection.IsLaunchedOutsideShell()` (in `Crosspose.Core.Diagnostics`) to reject double-click launches.
- Doctor checks follow the `ICheckFix` interface: `Name`, `CanFix`, `RunAsync`, `FixAsync` returning `CheckResult`/`FixResult` records.

## Deep Context (.context/)

The [.context/](.context/) directory contains detailed LLM-focused reference files:

| File | Covers |
|------|--------|
| [overview.md](.context/overview.md) | Project purpose, status, PowerShell prototype relationship |
| [architecture.md](.context/architecture.md) | Dependency graph, namespace map, inheritance hierarchy |
| [type-catalog.md](.context/type-catalog.md) | Every public type with full signatures and usage notes |
| [data-flow.md](.context/data-flow.md) | Process execution, container enumeration, helm rendering, doctor checks |
| [gui-internals.md](.context/gui-internals.md) | WPF windows, view models, converters, refresh loop |
| [cli-contracts.md](.context/cli-contracts.md) | CLI args, exit codes, output formats |
| [extension-points.md](.context/extension-points.md) | How to add checks, runners, views, pipeline stages |
| [conventions.md](.context/conventions.md) | Code patterns, naming, async usage, error handling |
| [external-tools.md](.context/external-tools.md) | Every external CLI invoked with exact arguments |
| [bugs.md](.context/bugs.md) | Confirmed broken behavior with fix guidance |
| [tech-debt.md](.context/tech-debt.md) | Structural issues to know when working in the codebase |
| [recommendations.md](.context/recommendations.md) | Prioritized action plan: fixes, cleanup, feature roadmap |

## PowerShell Prototype Reference

The original implementation lives at `C:\git\crossposeps`. Key files to reference when porting:
- `src/Main.ps1` — main conversion logic
- `assets/scripts/compose.ps1` — compose orchestration (start/stop/restart/status/validate)
- `docker-compose-outputs/` — sample output showing expected `docker-compose.<workload>.<os>.yml` file pattern
