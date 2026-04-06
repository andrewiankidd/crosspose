# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Crosspose?

Crosspose is a Windows-first dev tool that converts Helm/Kubernetes charts into Docker Compose stacks, then orchestrates Windows containers (Docker Desktop) and Linux containers (Podman in WSL) side-by-side. It brokers commands across both runtimes and presents a unified view via CLIs and WPF GUIs built on shared core logic.

## Build and Run

```powershell
# Build the entire solution
dotnet build Crosspose.sln

# Run tests
dotnet test Crosspose.sln

# Run individual projects
dotnet run --project src/Crosspose.Gui                     # Main WPF dashboard
dotnet run --project src/Crosspose.Doctor.Cli              # Prerequisite checker CLI
dotnet run --project src/Crosspose.Doctor.Cli -- --fix     # Auto-fix prerequisites
dotnet run --project src/Crosspose.Dekompose.Cli -- --chart <path> --values <values.yaml>
dotnet run --project src/Crosspose.Cli -- ps -a            # List containers across docker+podman
dotnet run --project src/Crosspose.Cli -- up --dir <path>  # Compose up across both platforms
```

Three test projects exist: `Crosspose.Core.Tests`, `Crosspose.Doctor.Tests`, `Crosspose.Dekompose.Tests` (xUnit, ~170 tests). No CI pipeline yet.

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
| **Crosspose.Ui** | WPF component library | [src/Crosspose.Ui/CLAUDE.md](src/Crosspose.Ui/CLAUDE.md) |
| **Crosspose.Cli** | CLI exe | [src/Crosspose.Cli/CLAUDE.md](src/Crosspose.Cli/CLAUDE.md) |
| **Crosspose.Dekompose** | Class library | [src/Crosspose.Dekompose/CLAUDE.md](src/Crosspose.Dekompose/CLAUDE.md) |
| **Crosspose.Dekompose.Cli** | CLI exe | [src/Crosspose.Dekompose.Cli/CLAUDE.md](src/Crosspose.Dekompose.Cli/CLAUDE.md) |
| **Crosspose.Dekompose.Gui** | WPF exe | [src/Crosspose.Dekompose.Gui/CLAUDE.md](src/Crosspose.Dekompose.Gui/CLAUDE.md) |
| **Crosspose.Doctor** | Class library | [src/Crosspose.Doctor/CLAUDE.md](src/Crosspose.Doctor/CLAUDE.md) |
| **Crosspose.Doctor.Cli** | CLI exe | [src/Crosspose.Doctor.Cli/CLAUDE.md](src/Crosspose.Doctor.Cli/CLAUDE.md) |
| **Crosspose.Doctor.Gui** | WPF exe | [src/Crosspose.Doctor.Gui/CLAUDE.md](src/Crosspose.Doctor.Gui/CLAUDE.md) |
| **Crosspose.Gui** | WPF exe | [src/Crosspose.Gui/CLAUDE.md](src/Crosspose.Gui/CLAUDE.md) |

**Crosspose.Core** — shared infrastructure: `ProcessRunner`, container runtime abstractions (Docker/Podman/Combined/WSL runners), `ComposeOrchestrator`, `HelmClient`, configuration (`crosspose.yml`), deployment services, networking (NAT gateway, port proxy, `PortProxyApplicator`), source management (Helm repos, OCI registries), and logging (console + Serilog file + in-memory with JWT/secret sanitization).

**Crosspose.Ui** — shared WPF component library. `LogViewerControl`, `AddChartSourceWindow`, `PickChartWindow`, `ChartSourceListItem`, `DoctorCheckPersistence`. References `Crosspose.Core` and `Crosspose.Doctor`.

**Crosspose.Dekompose** — Helm-to-Compose conversion library. `HelmTemplateRunner`, `ComposeGenerator` (YAML → compose files with OS splitting, port remapping, infra scaffolding).

**Crosspose.Dekompose.Cli** — CLI entry point for Dekompose. Parses args, invokes conversion pipeline.

**Crosspose.Dekompose.Gui** — WPF GUI for Dekompose. Chart/repo/values selection, runs conversion, manages chart sources. Accepts `--chart <path>` to open with a pre-supplied tgz.

**Crosspose.Cli** — unified CLI. `ps` aggregates docker+podman containers. `compose`/`up`/`down`/`restart`/`stop`/`start`/`logs`/`top`/`ps` orchestrate across both platforms. `sources` manages Helm/OCI chart sources.

**Crosspose.Doctor** — prerequisite check library. `ICheckFix` interface (includes `RequiresConnectivity`, `AutoFix`, `CheckIntervalSeconds` default members), `CheckCatalog` with 21 built-in checks (DockerCompose, DockerRunning, DockerWindowsMode, HnsNatHealth, OrphanedDockerNetwork, StalePortProxyConfig, WSL, WslMemoryLimit, WslNetworkingMode, StalePortProxy, Sudo, CrossposeWsl, PodmanWsl, PodmanCgroup, PodmanComposeWsl, Helm, AzureCli, PodmanHealthcheckRunner, PodmanCreatedContainer, PodmanContainerAutoheal, WslToWindowsFirewall). Supports additional checks via config. `offlineMode` parameter on `LoadAll` suppresses connectivity-requiring checks.

**Crosspose.Doctor.Cli** — CLI entry point for Doctor. `--fix` triggers remediation. `--enable-additional` for extra checks.

**Crosspose.Doctor.Gui** — WPF GUI for Doctor with per-item Fix buttons, Fix All window, offline mode amber banner, dark/light theme support.

**Crosspose.Gui** — main WPF dashboard. Sidebar: Charts, Compose Bundles, Projects, Containers, Images, Volumes. Tools menu: Doctor.Gui, Dekompose.Gui, offline mode toggle, portable mode enabler. View menu: dark/light theme toggle. Container details with live logs. Dark/light theme support.

## Key Patterns

- All external tool execution goes through `ProcessRunner.RunAsync()` — never call `Process.Start()` directly.
- CLI entry points use `CrossposeEnvironment.IsShellAvailable` to reject double-click launches.
- Doctor checks follow the `ICheckFix` interface: `Name`, `Description`, `CanFix`, `IsAdditional`, `RequiresConnectivity`, `RunAsync`, `FixAsync`.
- Configuration is centralized in `crosspose.yml` via `CrossposeConfigurationStore` / `CrossposeEnvironment`.
- Portable mode: if `.portable` file exists beside the exe, all data goes to `.\AppData\crosspose\` instead of `%APPDATA%`. Enable via Tools > Enable Portable Mode in Crosspose.Gui.
- Offline mode: persisted as `offline-mode` in `crosspose.yml`. Filters out `RequiresConnectivity` checks. Toggle via Tools menu.
- Theme: dark/light mode persisted as `compose.gui.dark-mode` in `crosspose.yml`. Toggle via View menu.
- Logging: all sinks (console, file, in-memory) sanitize JWTs and bearer tokens via `SecretCensor`.
- Compose orchestration routes Windows compose files to `docker compose` and Linux files to `podman compose` inside WSL.
- NAT gateway bridging (Windows→Linux): Dekompose rewrites Windows env vars to `${NAT_GATEWAY_IP}`; Doctor's PortProxy check configures `netsh` port forwarding on the Docker nat interface.
- Reverse bridging (Linux→Windows): Dekompose rewrites Linux env vars referencing Windows services to `${WSL_HOST_IP}`; `PortProxyApplicator` configures reverse `netsh` port forwarding on the WSL vEthernet interface; `WslToWindowsFirewallCheck` ensures Hyper-V and Windows firewall allow the traffic.
- Portable mode propagation: GUI sets `CROSSPOSE_PORTABLE_ROOT` env var so child processes (Doctor.Gui, Dekompose.Gui) inherit portable mode.

## Deep Context (.context/)

The [.context/](.context/) directory contains detailed LLM-focused reference files:

| File | Covers |
|------|--------|
| [overview.md](.context/overview.md) | Project purpose and status |
| [architecture.md](.context/architecture.md) | Dependency graph, namespace map, project structure |
| [conventions.md](.context/conventions.md) | Code patterns, naming, async usage, error handling |
| [extension-points.md](.context/extension-points.md) | How to add checks, runners, views, pipeline stages |
| [external-tools.md](.context/external-tools.md) | Every external CLI invoked with exact arguments |
| [bugs.md](.context/bugs.md) | Confirmed broken behavior with fix guidance |
| [tech-debt.md](.context/tech-debt.md) | Structural issues to know when working in the codebase |
| [recommendations.md](.context/recommendations.md) | Prioritized action plan |

## Documentation

The [docs/](docs/) directory contains user-facing documentation:
- [docs/README.md](docs/README.md) — overview, rationale, project links
- [docs/setup.md](docs/setup.md) — prerequisites and installation
- [docs/configuration.md](docs/configuration.md) — `crosspose.yml` schema, portable mode
- [docs/examples.md](docs/examples.md) — usage examples
- Per-project docs under `docs/<project>/README.md`
