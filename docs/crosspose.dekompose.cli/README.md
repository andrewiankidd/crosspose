# Crosspose.Dekompose.Cli

Thin CLI entry point that drives the Dekompose conversion pipeline. Parses arguments and invokes `Crosspose.Dekompose` to render a Helm chart into Docker Compose files.

## Usage

```powershell
dotnet run --project src/Crosspose.Dekompose.Cli -- --chart <path> [options]
```

## Flags

| Flag | Description |
|------|-------------|
| `--chart <path>` | Helm chart directory, `.tgz`, or OCI ref (`oci://host/repo[:tag]`) |
| `--chart-version <v>` | Chart version (OCI/repo charts) |
| `--values <file>` | Values file for Helm templating |
| `--dekompose-config <file>` | Additional `dekompose.yml` merged before rendering |
| `--manifest <file>` | Skip Helm; use an already-rendered manifest |
| `--output <dir>` | Base output folder (default: `./dekompose-outputs`) |
| `--compress` | Also write a `.zip` of the generated output |
| `--infra` | Apply matched `dekompose.custom-rules` and emit infra compose files |
| `--remap-ports` | Rewrite in-cluster service URLs to assigned host ports |

## Prerequisites
- `helm` on PATH (v3+).
- For OCI charts in Azure Container Registry: `az` CLI on PATH, logged in (`az login`).

## Related docs
- [Crosspose.Dekompose](../crosspose.dekompose/README.md) for output layout, custom rules, and pipeline internals.
- [Crosspose.Dekompose.Gui](../crosspose.dekompose.gui/README.md) for the WPF front-end.
