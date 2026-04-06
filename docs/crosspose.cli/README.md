# Crosspose.Cli

This page focuses on how the CLI discovers compose projects, splits Windows vs Linux workloads, and feeds execution context to Docker Desktop and Podman.

## Compose project discovery
Crosspose looks for `docker-compose.*.yml` files in the provided directory (or a zip bundle). It infers:

- Workload name: the second-to-last segment in `docker-compose.<workload>.<os>.yml`.
- Platform: files ending in `*.windows.yml` route to Docker Desktop; `*.linux.yml` route to Podman in WSL.
- Project name: defaults to the directory name unless `--project` is supplied.

If the directory contains multiple workloads, the CLI can filter to a single workload with `--workload <name>`.

## Execution model
For each action, Crosspose runs two commands in parallel where applicable:

- Docker Desktop (Windows): `docker compose ...`
- Podman inside WSL: `wsl --distribution <distro> -- podman compose ...`

Additional arguments after `--` are passed through verbatim, so you can use compose flags that Crosspose does not explicitly model.

## NAT gateway injection
When running `up` for Docker workloads, Crosspose resolves the Windows NAT gateway and injects `NAT_GATEWAY_IP` into the `docker compose` environment. This enables Windows containers to reach Linux services hosted in WSL by substituting `{{INFRA[...].HOSTNAME}}` with `${NAT_GATEWAY_IP}` during Dekompose output generation.

Gateway resolution order:
1) `conversion-report.yaml` network name (preferred when present),
2) network name parsed from the Windows compose files,
3) default Docker NAT gateway lookup.

## Common failure modes
- No compose files found: ensure the directory contains `docker-compose.<workload>.<os>.yml`.
- Docker not in Windows mode: `docker compose` will fail to start Windows containers.
- WSL distro missing: Crosspose defaults to `crosspose-data` unless overridden in `crosspose.yml`.
- Port proxy missing: run Crosspose.Doctor `--fix` to configure `netsh interface portproxy` rules.

## Advanced examples
```powershell
# Target a specific workload in a multi-workload directory
dotnet run --project src/Crosspose.Cli -- compose up --dir C:\temp\dekompose-outputs --workload core -d

# Run from a zipped definition bundle
dotnet run --project src/Crosspose.Cli -- up --dir C:\temp\dekompose-outputs\bundle.zip -d
```

## Container management commands

`crosspose` provides full CLI parity with the GUI for managing individual containers, images, and volumes:

```powershell
# Container lifecycle
crosspose container start <name|id>
crosspose container stop <name|id>
crosspose container rm <name|id>
crosspose container logs <name|id> [--tail N]
crosspose container inspect <name|id>

# Images
crosspose images ls
crosspose images rm <id|name:tag>
crosspose images prune

# Volumes
crosspose volumes ls
crosspose volumes rm <name>
crosspose volumes prune
```

Containers are resolved by name or ID prefix across both Docker and Podman.

## Bundle and deployment management

```powershell
# Inspect locally stored artifacts
crosspose bundles list
crosspose deployments list
crosspose charts list

# Deploy a compose bundle (extract and register)
crosspose deploy C:\path\to\bundle.zip --project my-app

# Remove a deployed directory
crosspose remove --dir C:\path\to\deployment
```

## Podman restart behaviour

Podman rootless containers reuse the original container's network namespace on `start`/`restart`, meaning DNS entries are stale. Crosspose works around this by issuing `podman compose up --force-recreate -d` whenever a `start` or `restart` action targets the Podman platform. This tears down and recreates the container, giving it a fresh network context.

## See also
- [Crosspose.Gui](../crosspose.gui/README.md) for the WPF frontend.
- [Crosspose.Core](../crosspose.core/README.md) for shared runners and orchestration helpers.
