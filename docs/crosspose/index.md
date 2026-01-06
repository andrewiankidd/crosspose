# Crosspose CLI (orchestration)

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

## See also
- [Crosspose.Gui](../crosspose.gui/index.md) for the WPF frontend.
- [Crosspose.Core](../crosspose.core/index.md) for shared runners and orchestration helpers.
