# Extension Points

How to add new functionality to each part of the system.

## Adding a New Doctor Check

1. Create a new class in `src/Crosspose.Doctor/Checks/` implementing `ICheckFix`:
   ```csharp
   public sealed class MyCheck : ICheckFix
   {
       public string Name => "my-check";
       public bool CanFix => true; // or false if no automated fix

       public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken ct)
       {
           var result = await runner.RunAsync("mytool", "--version", cancellationToken: ct);
           return result.IsSuccess
               ? CheckResult.Success(result.StandardOutput)
               : CheckResult.Failure("mytool not found");
       }

       public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken ct)
       {
           var result = await runner.RunAsync("winget", "install -e --id MyTool.Id -h", cancellationToken: ct);
           return result.IsSuccess
               ? FixResult.Success("Installed via winget")
               : FixResult.Failure(result.StandardError);
       }
   }
   ```

2. Add it to the array in `CheckCatalog.LoadAll()` — position determines display order.

3. Both the Doctor CLI and Doctor.Gui will automatically pick it up (no other registration needed).

## Adding a New Container Runtime

1. Create a new class in `src/Crosspose.Core/Orchestration/` extending `ContainerPlatformRunnerBase`:
   ```csharp
   public sealed class MyRunner : ContainerPlatformRunnerBase
   {
       public MyRunner(ProcessRunner runner) : base("myruntime", runner) { }

       public override async Task<IReadOnlyList<ContainerProcessInfo>> GetContainersDetailedAsync(...)
       {
           // Parse myruntime-specific JSON output
       }
   }
   ```

2. Override `GetContainersDetailedAsync`, `GetImagesDetailedAsync`, `GetVolumesDetailedAsync` to parse the runtime's JSON output format.

3. To include it in the combined view, modify the code that constructs `CombinedContainerPlatformRunner` — currently in `Crosspose.Cli/Program.cs` and `Crosspose.Gui/MainWindow.xaml.cs`. Note: `CombinedContainerPlatformRunner` currently hardcodes two runners (docker + podman); extending to N runners would require refactoring it to accept a list.

## Adding a New GUI Sidebar View

1. Add a `<ListBoxItem>` to the sidebar in `MainWindow.xaml`.
2. Add a corresponding display element (ListView, TreeView, etc.) in the content area.
3. Add a case to `OnSidebarSelectionChanged` and `RefreshCurrentViewAsync` in `MainWindow.xaml.cs`.
4. Create a view model class (following `ImageRow`/`VolumeRow` pattern with `INotifyPropertyChanged`).
5. Add an `ObservableCollection` property on `MainWindow` and a `ShowXxxAsync` method.

## Adding Compose File Generation (the main porting target)

The conversion pipeline in Dekompose currently stops at `ComposeStubWriter`. To implement actual generation:

1. Create a new service in `src/Crosspose.Dekompose/Services/` (e.g., `ComposeGenerator`).
2. Parse the rendered Kubernetes manifest YAML (multi-document) — consider using `YamlDotNet` NuGet.
3. For each workload (Deployment/StatefulSet/DaemonSet):
   - Detect target OS from node selectors, tolerations, or image base.
   - Map container specs to compose service definitions.
   - Assign ports (the prototype has a port assignment algorithm).
   - Translate ConfigMaps/Secrets to environment variables or bind mounts.
4. Emit files following the pattern: `docker-compose.<workload>.<os>.yml`.
5. Emit a shared resources file for networks and volumes.
6. Wire it into `Program.cs` after the helm rendering step, replacing the `ComposeStubWriter` call.

Reference: `C:\git\crossposeps\src\Main.ps1` for the complete algorithm.

## Adding Compose Orchestration (CLI)

The `compose` command in Crosspose.Cli is a stub. To implement:

1. Create orchestration classes in `src/Crosspose.Core/Orchestration/` or `src/Crosspose.Cli/`.
2. Implement actions: `start`, `stop`, `restart`, `status`, `validate`.
3. Each action should:
   - Find compose files in the output directory by pattern.
   - Route Windows compose files to Docker Desktop (`docker compose -f ...`).
   - Route Linux compose files to Podman in WSL (`wsl podman compose -f ...`).
   - Handle network driver fixes, path translation, ACR auth.
4. Wire into the `compose` case in `Program.cs`.

Reference: `C:\git\crossposeps\assets\scripts\compose.ps1`.

## Environment Variables

| Variable | Used By | Purpose |
|----------|---------|---------|
| `GUI_REFRESH_INTERVAL` | Crosspose.Gui | Container refresh interval in seconds (default: 5) |
| `CROSSPOSE_WSL_USER` | CrossposeWslCheck | Username for the crosspose-data WSL distro (default: `crossposeuser`) |
| `CROSSPOSE_WSL_PASS` | CrossposeWslCheck | Password for the WSL distro user (default: `crossposepassword`) |
| `PROMPT` | All CLIs | Used in `LaunchedOutsideShell()` detection |
| `PSModulePath` | All CLIs | Used in `LaunchedOutsideShell()` detection |
