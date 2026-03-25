# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Crosspose?

Crosspose is a Windows-first dev tool that converts Helm/Kubernetes charts into Docker Compose stacks, then orchestrates Windows containers (Docker Desktop) and Linux containers (Podman in WSL) side-by-side. It brokers commands across both runtimes and presents a unified view via CLIs and WPF GUIs built on shared core logic.

## Build and Run

```powershell
# Build the entire solution
dotnet build Crosspose.sln

# Run individual projects
dotnet run --project src/Crosspose.Gui                     # Main WPF dashboard
dotnet run --project src/Crosspose.Doctor.Cli              # Prerequisite checker CLI
dotnet run --project src/Crosspose.Doctor.Cli -- --fix     # Auto-fix prerequisites
dotnet run --project src/Crosspose.Dekompose.Cli -- --chart <path> --values <values.yaml>
dotnet run --project src/Crosspose.Cli -- ps -a            # List containers across docker+podman
dotnet run --project src/Crosspose.Cli -- up --dir <path>  # Compose up across both platforms
```

There are no tests yet.

## Tech Stack

- .NET 10 (`net10.0`), C# with nullable reference types and implicit usings
- WPF (`net10.0-windows10.0.19041`) for GUI projects
- NuGet: `Microsoft.Extensions.*` (Logging, DI, Configuration), `YamlDotNet`, `Tomlyn`, `Serilog` (file logging), `FluentIcons.Wpf`
- Configuration via `crosspose.yml` (see `docs/configuration.md`)
- CLI projects use top-level statements for Program.cs entry points

## Architecture

Ten projects in `src/`, all under `Crosspose.sln`. Libraries hold reusable logic; `.Cli`/`.Gui` projects are thin entry points.

| Project | Type | CLAUDE.md |
|---------|------|-----------|
| **Crosspose.Core** | Class library | [src/Crosspose.Core/CLAUDE.md](src/Crosspose.Core/CLAUDE.md) |
| **Crosspose.Ui** | WPF control library | [src/Crosspose.Ui/CLAUDE.md](src/Crosspose.Ui/CLAUDE.md) |
| **Crosspose.Cli** | CLI exe | [src/Crosspose.Cli/CLAUDE.md](src/Crosspose.Cli/CLAUDE.md) |
| **Crosspose.Dekompose** | Class library | [src/Crosspose.Dekompose/CLAUDE.md](src/Crosspose.Dekompose/CLAUDE.md) |
| **Crosspose.Dekompose.Cli** | CLI exe | [src/Crosspose.Dekompose.Cli/CLAUDE.md](src/Crosspose.Dekompose.Cli/CLAUDE.md) |
| **Crosspose.Dekompose.Gui** | WPF exe | [src/Crosspose.Dekompose.Gui/CLAUDE.md](src/Crosspose.Dekompose.Gui/CLAUDE.md) |
| **Crosspose.Doctor** | Class library | [src/Crosspose.Doctor/CLAUDE.md](src/Crosspose.Doctor/CLAUDE.md) |
| **Crosspose.Doctor.Cli** | CLI exe | [src/Crosspose.Doctor.Cli/CLAUDE.md](src/Crosspose.Doctor.Cli/CLAUDE.md) |
| **Crosspose.Doctor.Gui** | WPF exe | [src/Crosspose.Doctor.Gui/CLAUDE.md](src/Crosspose.Doctor.Gui/CLAUDE.md) |
| **Crosspose.Gui** | WPF exe | [src/Crosspose.Gui/CLAUDE.md](src/Crosspose.Gui/CLAUDE.md) |

**Crosspose.Core** — shared infrastructure: `ProcessRunner`, container runtime abstractions (Docker/Podman/Combined/WSL runners), `ComposeOrchestrator`, `HelmClient`, configuration (`crosspose.yml`), deployment services, networking (NAT gateway, port proxy), source management (Helm repos, OCI registries), and logging (console + Serilog file + in-memory with JWT/secret sanitization).

**Crosspose.Ui** — shared WPF control library (`LogViewerControl`).

**Crosspose.Dekompose** — Helm-to-Compose conversion library. `HelmTemplateRunner`, `ComposeGenerator` (YAML → compose files with OS splitting, port remapping, infra scaffolding), `ComposeStubWriter`.

**Crosspose.Dekompose.Cli** — CLI entry point for Dekompose. Parses args, invokes conversion pipeline.

**Crosspose.Dekompose.Gui** — WPF GUI for Dekompose. Chart/repo/values selection, runs conversion, manages chart sources.

**Crosspose.Cli** — unified CLI. `ps` aggregates docker+podman containers. `compose`/`up`/`down`/`restart`/`stop`/`start`/`logs`/`top`/`ps` orchestrate across both platforms. `sources` manages Helm/OCI chart sources.

**Crosspose.Doctor** — prerequisite check library. `ICheckFix` interface, `CheckCatalog` with 18+ checks (DockerCompose, DockerRunning, DockerWindowsMode, WSL, WslMemoryLimit, Sudo, CrossposeWsl, PodmanWsl, PodmanCgroup, PodmanComposeWsl, Helm, AzureCli, AzureAcrAuth, PortProxy). Supports additional checks via config.

**Crosspose.Doctor.Cli** — CLI entry point for Doctor. `--fix` triggers remediation. `--enable-additional` for extra checks.

**Crosspose.Doctor.Gui** — WPF GUI for Doctor with per-item Fix buttons, dark/light theme support.

**Crosspose.Gui** — main WPF dashboard. Sidebar: Definitions, Projects, Containers, Images, Volumes. Tools menu launches Doctor.Gui and Dekompose.Gui. Container details with live logs. Dark/light theme support via FluentIcons.

## Key Patterns

- All external tool execution goes through `ProcessRunner.RunAsync()` — never call `Process.Start()` directly.
- CLI entry points use `CrossposeEnvironment.IsShellAvailable` to reject double-click launches.
- Doctor checks follow the `ICheckFix` interface: `Name`, `Description`, `CanFix`, `IsAdditional`, `RunAsync`, `FixAsync`.
- Configuration is centralized in `crosspose.yml` via `CrossposeConfigurationStore` / `CrossposeEnvironment`.
- Portable mode: if `.portable` file exists beside the exe, all data goes to `.\AppData\crosspose\` instead of `%APPDATA%`.
- Logging: all sinks (console, file, in-memory) sanitize JWTs and bearer tokens via `SecretCensor`.
- Compose orchestration routes Windows compose files to `docker compose` and Linux files to `podman compose` inside WSL.
- NAT gateway bridging: Dekompose rewrites Windows env vars to point to the NAT gateway; Doctor's PortProxy check configures `netsh` port forwarding.

## Deep Context (.context/)

The [.context/](.context/) directory contains detailed LLM-focused reference files:

| File | Covers |
|------|--------|
| [overview.md](.context/overview.md) | Project purpose, status, PowerShell prototype relationship |
| [architecture.md](.context/architecture.md) | Dependency graph, namespace map, project structure |
| [conventions.md](.context/conventions.md) | Code patterns, naming, async usage, error handling |
| [extension-points.md](.context/extension-points.md) | How to add checks, runners, views, pipeline stages |
| [external-tools.md](.context/external-tools.md) | Every external CLI invoked with exact arguments |
| [bugs.md](.context/bugs.md) | Confirmed broken behavior with fix guidance |
| [tech-debt.md](.context/tech-debt.md) | Structural issues to know when working in the codebase |
| [recommendations.md](.context/recommendations.md) | Prioritized action plan |

## Documentation

The [docs/](docs/) directory contains user-facing documentation:
- [docs/index.md](docs/index.md) — overview, rationale, project links
- [docs/setup.md](docs/setup.md) — prerequisites and installation
- [docs/configuration.md](docs/configuration.md) — `crosspose.yml` schema, portable mode
- [docs/examples.md](docs/examples.md) — usage examples
- Per-project docs under `docs/<project>/index.md`

## PowerShell Prototype Reference

The original implementation lives at `C:\git\crossposeps`. Key files to reference when porting:
- `src/Main.ps1` — main conversion logic
- `assets/scripts/compose.ps1` — compose orchestration (start/stop/restart/status/validate)
- `docker-compose-outputs/` — sample output showing expected `docker-compose.<workload>.<os>.yml` file pattern
