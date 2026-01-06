# Crosspose.Gui

This page dives deeper into the WPF shell layout, how it discovers definitions, and which commands it issues under the hood.

## Navigation model
The sidebar is split into setup and runtime sections:

- Setup: Definitions (Dekompose outputs).
- Runtime: Projects, Containers, Images, Volumes.

Each view swaps toolbars and data sources based on the selection, so the same window can manage compose inputs and runtime state.

## Definitions and deployments
Definitions point at folders or zip bundles that contain `docker-compose.<workload>.<os>.yml` files. When you deploy a definition, the GUI extracts it into `compose.deployment-directory` and creates a project entry. That project is the unit for future `up`, `down`, `restart`, `logs`, and `ps` actions.

The definitions toolbar surfaces:
- New, Deploy, Delete, Open Folder
- Refresh (reloads definitions from disk)

## Projects view
Projects are derived from the deployment folders. Selecting a project enables the compose action buttons (Up, Down, Restart, Stop, Start, Remove). These actions call into `Crosspose.Core.Orchestration.ComposeOrchestrator`, which mirrors the CLI behavior for Docker and Podman.

## Containers view
The containers grid merges Docker and Podman containers into one list. Actions available today:

- Start, Stop, Delete
- Details (opens `ContainerDetailsWindow`)

The details window includes a live logs tab. Other tabs (inspect, mounts, exec, files, stats) are present but currently display placeholder text.

## Images and volumes
Images and Volumes are surfaced as aggregated lists from Docker and Podman. The toolbars expose Pull/Delete for images and basic refresh actions for volumes. These are thin wrappers over the same platform runners used by the CLI.

## Tools menu
The Tools menu launches:
- Crosspose Doctor GUI
- Crosspose Dekompose GUI
- Docker Desktop
- Podman Desktop

This keeps prerequisite checks, chart rendering, and upstream runtime controls within one shell.

## Dependencies and configuration
- Uses `Crosspose.Core` for orchestration, logging, and configuration.
- Respects `compose.deployment-directory` and `compose.gui.refresh-interval-seconds` from `crosspose.yml`.

## Related docs
- [Crosspose CLI](../crosspose/index.md) for orchestration behavior.
- [Crosspose.Dekompose.Gui](../crosspose.dekompose.gui/index.md) for chart-to-compose UI.
- [Crosspose.Doctor.Gui](../crosspose.doctor.gui/index.md) for prerequisite checks.
