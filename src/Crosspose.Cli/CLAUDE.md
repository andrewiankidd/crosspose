# CLAUDE.md — Crosspose.Cli

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Unified CLI for container operations and compose orchestration across Docker Desktop (Windows) and Podman (WSL). Intended to expose full parity with the GUI — every GUI action has a CLI equivalent.

## Commands

### Container orchestration
- `ps [--all|-a]` — aggregate docker+podman container list.
- `compose <action> [--dir <path>] [--workload <name>] [-d] [--project <name>] [-- <extra>]` — orchestrate compose across both platforms.
- `up|down|restart|stop|start|logs|top [args...]` — shorthand for `compose <action>`.
- `remove --dir <path>` — delete a deployment directory (opposite of `deploy`).
- `deploy <bundle.zip> [--project <name>] [--version <v>]` — extract a compose bundle and register it as a deployment.

### Container management
- `container start <name|id>` — start a container.
- `container stop <name|id>` — stop a container.
- `container rm <name|id>` — remove a container.
- `container logs <name|id> [--tail N]` — show container logs.
- `container inspect <name|id>` — show container details as JSON.

### Image management
- `images ls` — list all images across docker and podman.
- `images rm <id|name:tag>` — remove an image.
- `images prune` — remove all unused images (prompts for confirmation).

### Volume management
- `volumes ls` — list all volumes across docker and podman.
- `volumes rm <name>` — remove a volume.
- `volumes prune` — remove all unused volumes (prompts for confirmation).

### Local store inspection
- `bundles list` — list compose bundle zips in the output directory.
- `bundles rm <name>` — remove a bundle zip.
- `deployments list` — list deployed projects and versions.
- `charts list` — list Helm chart tarballs in the charts directory.

### Chart sources
- `sources list` — list configured Helm/OCI chart sources.
- `sources add <url> [--user <u>] [--pass <p>]` — add a chart source.
- `sources charts <name> [--user <u>] [--pass <p>]` — list charts from a source.

### Meta
- `--help` — full usage text.
- `--version` — print version.

## Run

```powershell
dotnet run --project src/Crosspose.Cli -- ps -a
dotnet run --project src/Crosspose.Cli -- up --dir C:\temp\dekompose-outputs\my-workload -d
dotnet run --project src/Crosspose.Cli -- container rm core-app
dotnet run --project src/Crosspose.Cli -- images ls
dotnet run --project src/Crosspose.Cli -- deployments list
dotnet run --project src/Crosspose.Cli -- sources list
```

## Notes

- Container operations resolve containers by name OR ID prefix across both docker and podman.
- Qualified IDs in the form `docker:<id>` or `podman:<id>` are used internally when dispatching to the correct runtime.
- Podman `start`/`restart` uses `up --force-recreate -d` under the hood (via `ComposeOrchestrator`) to avoid stale network namespace issues with rootless Podman.

## Dependencies

- `Crosspose.Core` — runners, orchestrator, sources, configuration, logging, deployment.
- `Crosspose.Doctor` — used for inline prerequisite checks before compose actions.
