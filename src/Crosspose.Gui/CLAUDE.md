# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this project.

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Main WPF GUI application for Crosspose. Provides a container dashboard across Docker and Podman, with sidebar navigation for Containers, Images, and Volumes views.

## Build and Run

```powershell
dotnet run --project src/Crosspose.Gui
```

## Architecture

- `MainWindow.xaml` — sidebar navigation (Containers/Images/Volumes), TreeView for containers grouped by project (`ProjectGroupRow` → `ContainerRow`), ListView for images and volumes.
- `LogWindow` — displays live logs via `InMemoryLogStore` from Core.
- `ContainerDetailsWindow` — shows details for a selected container.
- Menu bar launches Doctor.Gui, Docker Desktop, and Podman Desktop as external processes.
- Targets `net10.0-windows10.0.19041` with WPF (`UseWPF=true`).

## Doctor.Gui Integration

The csproj copies `Crosspose.Doctor.Gui` output into its own bin directory so it can launch it via the Tools menu. This is a `Content` copy, not a project reference with assembly loading.

## Dependencies

- `Crosspose.Core` — for container runner abstractions, `ProcessRunner`, and logging.
- `Crosspose.Doctor.Gui` — output files copied at build time (not a runtime assembly reference).
