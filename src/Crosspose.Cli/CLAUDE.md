# CLAUDE.md — Crosspose.Cli

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Unified CLI for container operations and compose orchestration across Docker Desktop (Windows) and Podman (WSL).

## Commands

- `ps [--all|-a]` — aggregate docker+podman container list.
- `compose <action> [--dir <path>] [--workload <name>] [-d] [--project <name>] [-- <extra>]` — orchestrate compose across both platforms.
- `up|down|restart|stop|start|logs|top [args...]` — shorthand for `compose <action>`.
- `sources list` — list configured Helm/OCI chart sources.
- `sources add <url> [--user <u>] [--pass <p>]` — add a chart source.
- `sources charts <name> [--user <u>] [--pass <p>]` — list charts from a source.
- `--help`, `--version`

## Run

```powershell
dotnet run --project src/Crosspose.Cli -- ps -a
dotnet run --project src/Crosspose.Cli -- up --dir C:\temp\dekompose-outputs\my-workload -d
dotnet run --project src/Crosspose.Cli -- sources list
```

## Dependencies

- `Crosspose.Core` — runners, orchestrator, sources, configuration, logging.
- `Crosspose.Doctor` — used for inline prerequisite checks before compose actions.
