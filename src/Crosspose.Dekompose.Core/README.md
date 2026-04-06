# Crosspose.Dekompose

Dekompose ingests a Helm chart + values (or a pre-rendered manifest) and emits workload/OS-specific Docker Compose files such as `docker-compose.<workload>.<os>.yml`. The name is a nod to [Kompose](https://kompose.io/) (docker-compose -> Kubernetes); Dekompose runs the inverse path from Helm/Kubernetes -> Compose.

## Current state
- Runs `helm template` when `--chart` is provided; otherwise consumes `--manifest`/`--rendered-manifest`.
- Writes `docker-compose.<workload>.<os>.yml` files plus `conversion-report.yaml` for infra/port proxy metadata.
- When templating charts, persists `_chart.yaml` and `_values.yaml` snapshots into the output directory.

## Commands & options
- `--chart <path>`: Helm chart directory to template.
- `--values <file>`: Optional `values.yaml` passed to helm.
- `--manifest <file>` / `--rendered-manifest <file>`: Use a pre-rendered manifest instead of running helm.
- `--output <dir>`: Output folder (default: `./dekompose-outputs`).
- `--help`: Show help text.
- `--version`, `-v`: Show version.

## Usage examples
```powershell
# Render via helm
dotnet run --project src/Crosspose.Dekompose -- --chart C:\path\to\chart --values C:\path\to\values.yaml

# Use a pre-rendered manifest (skips helm)
dotnet run --project src/Crosspose.Dekompose -- --manifest C:\path\to\manifest.yaml

# Custom output directory
dotnet run --project src/Crosspose.Dekompose -- --chart C:\path\to\chart --output C:\temp\dekompose-outputs
```

## Roadmap / porting targets
- Expand workload detection, OS split, port assignment, ConfigMap/Secret handling, infra detection.
