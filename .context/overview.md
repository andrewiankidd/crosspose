# Project Overview

## What Crosspose Does

Crosspose is a Windows-first dev tool that:
1. **Dekompose**: Takes Helm/Kubernetes charts and converts them into Docker Compose files split by workload and OS (Windows vs Linux). Supports infrastructure scaffolding (MSSQL, etc.), service port remapping, and chart-specific translation rules.
2. **Orchestrate**: Runs those compose stacks side-by-side — Windows containers on Docker Desktop, Linux containers on Podman in WSL — with NAT gateway bridging between them.
3. **Unified view**: Provides CLIs and WPF GUIs for container management, prerequisite checking, and conversion.

The target user is a developer running hybrid Windows/Linux workloads locally on a Windows machine.

## Current Status

- **Working**: Full compose generation pipeline (ComposeGenerator), compose orchestration (up/down/restart/stop/start/logs/top/ps), full CLI parity (container/images/volumes/bundles/deployments/charts subcommands), deploy/remove commands, 20+ Doctor checks with fixes, container dashboard with live logs, Charts view with pull-from-source, Dekompose GUI with chart source management, Helm/OCI source support, portable mode, offline mode, dark/light themes, Serilog file logging with secret sanitization, NAT gateway bridging with port proxy, image/volume pruning. Podman start/restart uses `up --force-recreate` to avoid stale network namespace. Job-type K8s resources emit `service_completed_successfully` depends_on conditions.
- **In progress**: CI/CD pipeline.
- **Done**: Test suite (~170 tests across `Crosspose.Core.Tests`, `Crosspose.Doctor.Tests`, `Crosspose.Dekompose.Tests`).

## Tech Stack

- .NET 10 (`net10.0`), C# 13, nullable reference types
- WPF for GUI projects (`net10.0-windows10.0.19041`)
- NuGet: `Microsoft.Extensions.*`, `YamlDotNet`, `Tomlyn`, `Serilog`, `FluentIcons.Wpf`
- Configuration via `crosspose.yml` (YAML, with portable mode support)
- 10 projects in `Crosspose.sln` + 3 test projects
