# Crosspose.Core

Crosspose.Core provides the shared building blocks used by every CLI and GUI. This page focuses on the internal services and how they interact.

## Orchestration
- `ComposeOrchestrator`: Executes docker and podman compose actions, injects `NAT_GATEWAY_IP`, and aggregates results.
- `ComposeProjectLoader`: Discovers `docker-compose.<workload>.<os>.yml` files and splits workloads by platform.
- Container/image/volume runners: Normalize Docker and Podman APIs into a single model for the UI.

## Networking
- `NatGatewayResolver`: Resolves the Windows NAT gateway using network metadata or Docker NAT inspection.
- `PortProxyRequirementLoader`: Reads `conversion-report.yaml` to detect which ports require Windows port proxy rules.
- `WindowsNatUtilities`: Provides low-level NAT gateway helpers.

## Helm and sources
- `HelmClient` and `HelmTemplateRunner`: Wrap `helm` and render charts with structured output parsing.
- `OciSourceClient` and source stores: Manage Helm repo and OCI registry state for Dekompose and the GUIs.

## Configuration and logging
- `CrossposeConfigurationStore`: Loads and saves `crosspose.yml` in `%APPDATA%` (or `.\AppData\crosspose` in portable mode).
- `DoctorCheckRegistrar`: Persists `doctor.additional-checks` whenever tools discover new requirements.
- `CrossposeLoggerFactory` and log store: Unified logging across CLI and GUI layers.

## Consumers
- Crosspose CLI and GUI (orchestration and status).
- Dekompose CLI/GUI (Helm rendering and process execution).
- Doctor CLI/GUI (checks and fixes).

## Related docs
- [Crosspose](../crosspose/index.md) for orchestration behavior.
- [Crosspose.Dekompose](../crosspose.dekompose/index.md) for chart rendering.
