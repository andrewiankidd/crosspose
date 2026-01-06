# Crosspose CLI

Crosspose CLI is the orchestration layer that replaces direct `docker`/`podman` usage. It runs compose actions across Windows Docker Desktop and WSL Podman to manage hybrid Windows/Linux workloads side-by-side.

## Current state
- `ps` aggregates `docker ps` and `podman ps` output (if the commands exist on PATH).
- `compose` orchestrates compose actions across both platforms.
- `sources` manages Helm/OCI chart sources used by orchestration.

## Commands & options
- `ps [--all|-a]`: Show docker and podman containers side-by-side (running only by default).
- `compose [action] [--dir <path>] [--workload <name>] [-d] [--project <name>] [-- <extra args>]`: Run compose across docker + podman. Supported actions: `up`, `down`, `restart`, `stop`, `start`, `logs`, `top`, `ps`.
- `up|down|restart|stop|start|logs|top [args...]`: Shorthand for `compose <action> ...`.
- `sources list`: List configured chart sources (Helm and OCI).
- `sources add <url> [--user|-u <user>] [--pass|-p <pass>]`: Detect and add a Helm repo or OCI registry.
- `sources charts <sourceName> [--user|-u <user>] [--pass|-p <pass>]`: List charts from a configured source.
- `--help`: Show help text.
- `--version`, `-v`: Show version.

## Usage examples
```powershell
# List containers from Docker and Podman
dotnet run --project src/Crosspose.Cli -- ps -a

# Compose orchestration
dotnet run --project src/Crosspose.Cli -- compose up --dir C:\temp\dekompose-outputs\my-workload -d

# Shorthand compose action
dotnet run --project src/Crosspose.Cli -- up --dir C:\temp\dekompose-outputs\my-workload -d

# Manage chart sources
dotnet run --project src/Crosspose.Cli -- sources add https://example.com/charts --user me --pass p@ss
dotnet run --project src/Crosspose.Cli -- sources charts example
```
