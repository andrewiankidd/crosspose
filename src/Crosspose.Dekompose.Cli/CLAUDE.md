# CLAUDE.md — Crosspose.Dekompose.Cli

See also: [root CLAUDE.md](../../CLAUDE.md) | [Dekompose library](../Crosspose.Dekompose.Core/CLAUDE.md)

## Purpose

Thin CLI entry point for Dekompose. Parses args, reads chart metadata via `TryReadChartInfo` (matches only root-level `Chart.yaml` to avoid subchart names), invokes `HelmTemplateRunner` + `ComposeGenerator`, optionally compresses output. `ChartInfo` carries both the internal chart name and `TgzName` (derived from the tgz filename) so rule matching works against either.

## Arguments

- `--chart <path>` — Helm chart directory or OCI reference.
- `--chart-version <v>` — optional chart version for helm.
- `--values <file>` — optional values.yaml.
- `--dekompose-config <file>` — optional dekompose.yml to merge.
- `--manifest <file>` — pre-rendered manifest (skips helm).
- `--output <dir>` — output folder.
- `--compress` — zip the output and remove the folder.
- `--infra` — scaffold supporting infrastructure.
- `--remap-ports` — remap in-cluster service URLs to localhost.
- `--help`, `--version`

## Dependencies

- `Crosspose.Core`, `Crosspose.Dekompose`
