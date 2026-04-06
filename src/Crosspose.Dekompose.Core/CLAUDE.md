# CLAUDE.md — Crosspose.Dekompose.Core

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Helm-to-Compose conversion library. Renders Helm charts and generates Docker Compose files split by workload and OS (Windows vs Linux). Used by both `Crosspose.Dekompose.Cli` and `Crosspose.Dekompose.Gui`.

## Services (namespace: `Crosspose.Dekompose.Core.Services`)

- **`HelmTemplateRunner`** — shells out to `helm template`, writes rendered YAML.
- **`ComposeGenerator`** — parses rendered K8s manifests, detects workload OS, assigns ports, translates ConfigMaps/Secrets, emits per-workload compose files. Supports infrastructure scaffolding (e.g. MSSQL) and service port remapping via `DekomposeRuleSet` config.

## Dependencies

- `Crosspose.Core` — for `ProcessRunner`, configuration, logging.
- `YamlDotNet` — YAML manifest parsing.
