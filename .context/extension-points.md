# Extension Points

## Adding a New Doctor Check

1. Create a class implementing `ICheckFix` in `src/Crosspose.Doctor/Checks/`.
2. Set `IsAdditional = false` for always-on checks, or `true` with an `AdditionalKey` for opt-in checks.
3. Add to the list in `CheckCatalog.LoadAll()`.
4. For additional checks with parameters (like `azure-acr-auth-win:<registry>`), add parsing logic in `CheckCatalog.LoadAll()`.
5. Doctor.Cli, Doctor.Gui, and Dekompose.Gui all pick it up automatically.

## Adding a New Container Runtime

1. Extend `ContainerPlatformRunnerBase` in `Crosspose.Core/Orchestration/`.
2. Override `GetContainersDetailedAsync`, `GetImagesDetailedAsync`, `GetVolumesDetailedAsync` to parse the runtime's JSON.
3. Wire into `CombinedContainerPlatformRunner` (currently hardcoded to docker + podman).
4. Update `ComposeOrchestrator` to route compose actions to the new runtime.

## Adding Dekompose Translation Rules

Rules are defined in `crosspose.yml` under `dekompose.custom-rules` and loaded via `CrossposeEnvironment.GetDekomposeRules()`. Each `DekomposeRuleSet` can specify infrastructure definitions (e.g., MSSQL containers to scaffold) and secret mappings.

## Adding a CLI Command

1. Add a case to the `switch (command)` in the relevant CLI's `Program.cs`.
2. For Crosspose.Cli, compose action shorthands (`up`, `down`, etc.) are already routed to `ComposeOrchestrator`.
3. Update `PrintUsage()` and the project's CLAUDE.md / README.

## Adding a GUI Sidebar View (Crosspose.Gui)

1. Add a `<ListBoxItem>` to the appropriate sidebar group in `MainWindow.xaml`.
2. Add a corresponding display element in the content area.
3. Add a case to `OnSidebarSelectionChanged` and `RefreshCurrentViewAsync` in `MainWindow.xaml.cs`.
4. Create a view model class and `ObservableCollection` property.

## Configuration

All configuration lives in `crosspose.yml`. See `docs/configuration.md` for the full schema. Key extension points:
- `doctor.additional-checks` — enable additional Doctor checks by key.
- `dekompose.custom-rules` — chart-specific compose translation rules.
- `compose.wsl.*` — WSL distro/user/password defaults.
- `oci-registries` — OCI registry list for chart sources.
