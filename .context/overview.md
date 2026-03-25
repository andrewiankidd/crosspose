# Project Overview

## What Crosspose Does

Crosspose is a Windows-first dev tool that:
1. Takes Helm/Kubernetes charts and converts them into Docker Compose files split by workload and OS (Windows vs Linux).
2. Orchestrates those compose stacks side-by-side: Windows containers on Docker Desktop, Linux containers on Podman in WSL.
3. Provides a unified view of containers across both runtimes.

The target user is a developer running hybrid Windows/Linux workloads locally on a Windows machine.

## Current Status (PoC)

This is a .NET rewrite of a PowerShell prototype at `C:\git\crossposeps`. The rewrite is partial:

- **Working**: Doctor checks (prerequisite validation + auto-fix), container listing across docker+podman, helm template rendering, GUI container dashboard with start/stop.
- **Stub**: Compose file generation (outputs a placeholder TODO file), compose orchestration (start/stop/restart/status), GUI container details tabs (Logs/Inspect/Exec/Files/Stats).
- **Not started**: WinUI 3 migration (Gui currently uses WPF), test suite, CI/CD.

## Tech Stack

- .NET 10 preview (`net10.0`), C# 13, nullable reference types enabled
- WPF for GUI projects (`net10.0-windows10.0.19041`)
- Only Microsoft.Extensions.* NuGet packages (Logging, DI, Configuration)
- No test framework yet
- Solution: `Crosspose.sln` with 6 projects in `src/`

## Relationship to PowerShell Prototype

The prototype at `C:\git\crossposeps` is the source of truth for:
- Compose file generation logic (`src/Main.ps1`)
- Compose orchestration flows (`assets/scripts/compose.ps1`)
- Expected output format (`docker-compose-outputs/docker-compose.<workload>.<os>.yml`)

When porting, the .NET code should produce identical compose output to the prototype.

## Repo Layout Inspiration

The multi-project layout follows the pattern from `C:\git\IdleOps`.
