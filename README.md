# Crosspose

![logo](assets/logo.png)

Crosspose is a Windows-first toolchain for turning Helm/Kubernetes workloads into runnable Docker Compose stacks and orchestrating them side-by-side on Docker Desktop (Windows containers) and Podman in WSL.

## Components

Libraries hold reusable logic; `.Cli`/`.Gui` projects are thin entry points.

- `src/Crosspose.Core` – Shared infrastructure: process runner, container runtime abstractions, logging.
- `src/Crosspose.Dekompose` – Conversion library: renders Helm charts and emits workload-split compose files.
- `src/Crosspose.Dekompose.Cli` – CLI entry point for Dekompose.
- `src/Crosspose.Cli` – Unified CLI for container operations (`crosspose ps -a`, `crosspose compose ...`).
- `src/Crosspose.Doctor` – Prerequisite check library (like `flutter doctor`): docker compose, WSL, helm.
- `src/Crosspose.Doctor.Cli` – CLI entry point for Doctor. `--fix` attempts automated remediation.
- `src/Crosspose.Doctor.Gui` – WPF UI for Doctor with per-item Fix buttons.
- `src/Crosspose.Gui` – Main WPF dashboard: container/image/volume views, Charts view, launches Doctor.Gui via Tools menu.

## Documentation

See [docs/](docs/) for user-facing documentation.

## Quick start

Prefer the GUI — **run elevated** so Doctor can auto-configure port proxies and restart services without per-operation UAC prompts:
```powershell
Start-Process powershell -Verb RunAs -ArgumentList '-NoExit','-Command','dotnet run --project src/Crosspose.Gui'
```

Without elevation, Crosspose still works but Doctor fixes that need admin (port proxy rules, service restarts) will prompt UAC individually.

If you prefer CLI:
```powershell
# 1) Check prerequisites
dotnet run --project src/Crosspose.Doctor.Cli

# 2) Attempt automatic fixes (winget/wsl where possible)
dotnet run --project src/Crosspose.Doctor.Cli -- --fix

# 3) Render a chart and scaffold compose outputs
dotnet run --project src/Crosspose.Dekompose.Cli -- --chart C:\path\to\chart --values C:\path\to\values.yaml

# 4) Compose orchestration
dotnet run --project src/Crosspose.Cli -- compose --action status

# 5) View running containers across docker/podman
dotnet run --project src/Crosspose.Cli -- ps -a
```
