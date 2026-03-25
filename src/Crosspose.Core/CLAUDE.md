# CLAUDE.md — Crosspose.Core

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Shared class library that every other Crosspose project depends on. Provides process execution, container runtime abstractions, compose orchestration, configuration, deployment, networking, source management, and logging.

## Key Namespaces

- **`Configuration`** — `CrossposeEnvironment` (centralized env/config access), `CrossposeConfigurationStore` (loads `crosspose.yml`), `AppDataLocator` (portable mode support), `DekomposeConfiguration` (rule sets for compose generation).
- **`Deployment`** — `DefinitionDeploymentService` (extract and deploy compose projects), `DeploymentMetadataStore`, `PortProxyRequirementLoader`.
- **`Diagnostics`** — `ProcessRunner` (async process execution with stdout/stderr capture, secret-sanitized output handler), `ProcessResult`.
- **`Logging`** — `CrossposeLoggerFactory` (console + Serilog file + optional in-memory, all with JWT/bearer sanitization via `SecretCensor`), `InMemoryLogStore`.
- **`Networking`** — `NatGatewayResolver`, `WindowsNatUtilities` (for Docker↔WSL port bridging).
- **`Orchestration`** — Container runners (`DockerContainerRunner`, `PodmanContainerRunner`, `CombinedContainerPlatformRunner`, `WslRunner`), `ComposeOrchestrator` (routes compose actions to docker/podman), `ComposeProjectLoader`, `HelmClient`, `HelmRepositoryStore`.
- **`Sources`** — `HelmSourceClient`, `OciSourceClient`, `SourceAuthHelper`, `SourceNameGenerator` for managing chart repositories and OCI registries.

## Dependencies

- `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Logging.Console`
- `YamlDotNet` (YAML config parsing)
- `Serilog.Extensions.Logging`, `Serilog.Sinks.File` (file logging)
