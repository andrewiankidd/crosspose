# Architecture

## Solution Structure

```
Crosspose.sln
└── src/
    ├── Crosspose.Core/              # Class library — shared infrastructure
    ├── Crosspose.Ui/                # WPF component library — shared UI windows/controls
    ├── Crosspose.Cli/               # Console exe — unified container/compose CLI
    ├── Crosspose.Dekompose.Core/    # Class library — helm-to-compose conversion
    ├── Crosspose.Dekompose.Cli/     # Console exe — Dekompose CLI entry point
    ├── Crosspose.Dekompose.Gui/     # WPF exe — Dekompose GUI
    ├── Crosspose.Doctor.Core/       # Class library — prerequisite checks
    ├── Crosspose.Doctor.Cli/        # Console exe — Doctor CLI entry point
    ├── Crosspose.Doctor.Gui/        # WPF exe — Doctor GUI
    ├── Crosspose.Gui/               # WPF exe — main dashboard
    ├── Crosspose.Core.Tests/        # xUnit tests for Core
    ├── Crosspose.Doctor.Tests/      # xUnit tests for Doctor
    └── Crosspose.Dekompose.Tests/   # xUnit tests for Dekompose
```

## Naming Convention

- `Crosspose.X.Core` — library with reusable logic (e.g. `Crosspose.Doctor.Core`, `Crosspose.Dekompose.Core`)
- `Crosspose.X.Cli` — thin CLI entry point (Program.cs + arg parsing)
- `Crosspose.X.Gui` — thin GUI entry point (WPF windows)
- `Crosspose.X.Tests` — xUnit test project

## Dependency Graph

```
Crosspose.Core  (no project references)
    ↑
    ├── Crosspose.Ui ──→ Crosspose.Core, Crosspose.Doctor.Core
    │       (shared windows: AddChartSourceWindow, PickChartWindow, LogViewerControl)
    │
    ├── Crosspose.Cli ──→ Crosspose.Doctor.Core
    │
    ├── Crosspose.Dekompose.Core
    │       ↑
    │       ├── Crosspose.Dekompose.Cli
    │       └── Crosspose.Dekompose.Gui ──→ Crosspose.Doctor.Core, Crosspose.Ui
    │
    ├── Crosspose.Doctor.Core
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
| `Crosspose.Core.Networking` | `NatGatewayResolver`, `WindowsNatUtilities`, `PortProxyApplicator` |
| `Crosspose.Core.Orchestration` | `ComposeOrchestrator`, `ComposeProjectLoader`, `CombinedContainerPlatformRunner`, `DockerContainerRunner`, `PodmanContainerRunner`, `HelmClient`, `HelmRepositoryStore` |
| `Crosspose.Core.Sources` | `HelmSourceClient`, `OciSourceClient`, `SourceAuthHelper` |
| `Crosspose.Dekompose.Core.Services` | `HelmTemplateRunner`, `ComposeGenerator` |
| `Crosspose.Doctor.Core` | `CheckCatalog`, `DoctorSettings` |
| `Crosspose.Doctor.Core.Checks` | `ICheckFix`, 20+ check implementations |
| `Crosspose.Ui` | `LogViewerControl`, `AddChartSourceWindow`, `PickChartWindow`, `ChartSourceListItem`, `DoctorCheckPersistence` |

## Target Frameworks

| Project | TFM |
|---------|-----|
| Core, Dekompose.Core, Doctor.Core | `net10.0` |
| Cli, Dekompose.Cli, Doctor.Cli | `net10.0` |
| Ui, Gui, Dekompose.Gui, Doctor.Gui | `net10.0-windows10.0.19041` |
| Core.Tests, Doctor.Tests, Dekompose.Tests | `net10.0` |
