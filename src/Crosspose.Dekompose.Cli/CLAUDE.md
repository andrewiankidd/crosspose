# CLAUDE.md — Crosspose.Dekompose.Cli

See also: [root CLAUDE.md](../../CLAUDE.md) | [Dekompose library](../Crosspose.Dekompose/CLAUDE.md)

## Purpose

Thin CLI entry point for Dekompose. Parses args, reads chart metadata, invokes `HelmTemplateRunner` + `ComposeGenerator`, optionally compresses output.

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
