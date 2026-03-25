# Crosspose

![logo](assets/logo.png)

Crosspose is a Windows-first toolchain for turning Helm/Kubernetes workloads into runnable Docker Compose stacks and orchestrating them side-by-side on Docker Desktop (Windows containers) and Podman in WSL. This repo is the .NET rewrite of the PowerShell prototype in `C:\git\crossposeps`, and follows the multi-project layout style of `C:\git\IdleOps`.

## Components

Libraries hold reusable logic; `.Cli`/`.Gui` projects are thin entry points.

- `src/Crosspose.Core` – Shared infrastructure: process runner, container runtime abstractions, logging.
- `src/Crosspose.Dekompose` – Conversion library: renders Helm charts and will emit workload-split compose files.
- `src/Crosspose.Dekompose.Cli` – CLI entry point for Dekompose.
- `src/Crosspose.Cli` – Unified CLI for container operations (`crosspose ps -a`, `crosspose compose ...`).
- `src/Crosspose.Doctor` – Prerequisite check library (like `flutter doctor`): docker compose, WSL, helm.
- `src/Crosspose.Doctor.Cli` – CLI entry point for Doctor. `--fix` attempts automated remediation.
- `src/Crosspose.Doctor.Gui` – WPF UI for Doctor with per-item Fix buttons.
- `src/Crosspose.Gui` – Main WPF dashboard: container/image/volume views, launches Doctor.Gui via Tools menu.

## References
- Prototype logic & expected compose output: `C:\git\crossposeps` (see `src/Main.ps1`, `assets\scripts\compose.ps1`, `docker-compose-outputs\*`).
- Repo layout inspiration: `C:\git\IdleOps`.

## Quick start
Prefer the GUI:
```powershell
dotnet run --project src/Crosspose.Gui
```

If you prefer CLI:
```powershell
# 1) Check prerequisites
dotnet run --project src/Crosspose.Doctor.Cli

# 2) Attempt automatic fixes (winget/wsl where possible)
dotnet run --project src/Crosspose.Doctor.Cli -- --fix

# 3) Render a chart (or use --manifest to skip helm) and scaffold outputs
dotnet run --project src/Crosspose.Dekompose.Cli -- --chart C:\path\to\chart --values C:\path\to\values.yaml
# or: dotnet run --project src/Crosspose.Dekompose.Cli -- --manifest C:\path\to\rendered.yaml --output C:\temp\docker-compose-outputs

# 4) Compose orchestration (stub; delegates to PowerShell prototype today)
dotnet run --project src/Crosspose.Cli -- compose --action status

# 5) View running containers across docker/podman
dotnet run --project src/Crosspose.Cli -- ps -a
```

The `docker-compose-outputs` folder produced by Dekompose includes `TODO.compose-generation.md`, which links back to the PowerShell prototype and lists the next engineering steps to port the converter.

## Roadmap (initial cut)
- Port workload/OS detection, port assignment, and ConfigMap/Secret handling from `crossposeps` into `Dekompose`.
- Recreate orchestration behaviors from `assets\scripts\compose.ps1` inside `Crosspose.Cli` (start/stop/restart/status/validate against docker + podman).
- Add tests for the conversion pipeline and docker/podman command brokering.
