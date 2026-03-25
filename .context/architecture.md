# Architecture

## Solution Structure

```
Crosspose.sln
└── src/
    ├── Crosspose.Core/              # Class library — shared infrastructure
    ├── Crosspose.Ui/                # WPF control library — shared UI controls
    ├── Crosspose.Cli/               # Console exe — unified container/compose CLI
    ├── Crosspose.Dekompose/         # Class library — helm-to-compose conversion
    ├── Crosspose.Dekompose.Cli/     # Console exe — Dekompose CLI entry point
    ├── Crosspose.Dekompose.Gui/     # WPF exe — Dekompose GUI
    ├── Crosspose.Doctor/            # Class library — prerequisite checks
    ├── Crosspose.Doctor.Cli/        # Console exe — Doctor CLI entry point
    ├── Crosspose.Doctor.Gui/        # WPF exe — Doctor GUI
    └── Crosspose.Gui/               # WPF exe — main dashboard
```

## Naming Convention

- `Crosspose.X` — library with reusable logic
- `Crosspose.X.Cli` — thin CLI entry point (Program.cs + arg parsing)
- `Crosspose.X.Gui` — thin GUI entry point (WPF windows)

## Dependency Graph

```
Crosspose.Core  (no project references)
    ↑
    ├── Crosspose.Ui (no project references — pure WPF controls)
    │
    ├── Crosspose.Cli ──→ Crosspose.Doctor
    │
    ├── Crosspose.Dekompose
    │       ↑
    │       ├── Crosspose.Dekompose.Cli
    │       └── Crosspose.Dekompose.Gui ──→ Crosspose.Doctor, Crosspose.Ui
    │
    ├── Crosspose.Doctor
    │       ↑
    │       ├── Crosspose.Doctor.Cli
    │       └── Crosspose.Doctor.Gui
    │
    └── Crosspose.Gui ──→ Crosspose.Ui
            ──→ Crosspose.Doctor.Gui (output copy, not assembly ref)
            ──→ Crosspose.Dekompose.Gui (output copy, not assembly ref)
```

## Core Namespace Map

| Namespace | Key Types |
|-----------|-----------|
| `Crosspose.Core.Configuration` | `CrossposeEnvironment`, `CrossposeConfigurationStore`, `AppDataLocator`, `DekomposeConfiguration`, `DekomposeRuleSet` |
| `Crosspose.Core.Deployment` | `DefinitionDeploymentService`, `DeploymentMetadataStore`, `PortProxyRequirementLoader` |
| `Crosspose.Core.Diagnostics` | `ProcessRunner`, `ProcessResult` |
| `Crosspose.Core.Logging` | `CrossposeLoggerFactory`, `InMemoryLogStore`, `SecretCensor` |
| `Crosspose.Core.Networking` | `NatGatewayResolver`, `WindowsNatUtilities` |
| `Crosspose.Core.Orchestration` | `ComposeOrchestrator`, `ComposeProjectLoader`, `CombinedContainerPlatformRunner`, `DockerContainerRunner`, `PodmanContainerRunner`, `HelmClient`, `HelmRepositoryStore` |
| `Crosspose.Core.Sources` | `HelmSourceClient`, `OciSourceClient`, `SourceAuthHelper` |
| `Crosspose.Dekompose.Services` | `HelmTemplateRunner`, `ComposeGenerator`, `ComposeStubWriter` |
| `Crosspose.Doctor` | `CheckCatalog`, `DoctorSettings` |
| `Crosspose.Doctor.Checks` | `ICheckFix`, 18+ check implementations |

## Target Frameworks

| Project | TFM |
|---------|-----|
| Core, Dekompose, Doctor | `net10.0` |
| Cli, Dekompose.Cli, Doctor.Cli | `net10.0` |
| Ui, Gui, Dekompose.Gui, Doctor.Gui | `net10.0-windows10.0.19041` |
