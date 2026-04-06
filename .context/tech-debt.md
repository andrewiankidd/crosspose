# Tech Debt

Structural issues worth knowing about when working in the codebase. These are cleanup items, not urgent problems.

---

### Bare catch blocks across the codebase

Files: `ComposeOrchestrator.cs`, `AppDataLocator.cs`, `CrossposeConfigurationStore.cs`, `DeploymentMetadataStore.cs`, `ComposeProjectLoader.cs`, `WindowsNatUtilities.cs`, `OciSourceClient.cs`, `SourceNameGenerator.cs`, `Program.cs` (Cli + Dekompose.Cli), `MainWindow.xaml.cs` (Gui + Dekompose.Gui), `AddChartSourceWindow.xaml.cs`, `App.xaml.cs`.

Most are intentional (best-effort parsing, fallback to null), but they make debugging hard. Consider adding `catch (Exception ex) { logger.LogDebug(...) }` or at minimum a comment explaining why the catch is bare.

---

### `ProcessRunner` log helpers create tight coupling

**File**: `src/Crosspose.Core/Diagnostics/ProcessRunner.cs:17-21`

Container runners call `Runner.LogDebug(...)` instead of having their own logger. Runners depend on `ProcessRunner` for both execution and logging.

---

### `JsonDocument` instances not disposed in container runners

`DockerContainerRunner.EnumerateJsonElements` uses `yield return` preventing `JsonDocument` disposal. `PodmanContainerRunner` also skips `using`. With 5-second GUI refresh, undisposed documents create steady memory pressure.

**Files**: `DockerContainerRunner.cs:148-170`, `PodmanContainerRunner.cs`

---

### Static `HttpClient` instances (5 locations)

`HelmClient`, `OciRegistryStore`, `HelmSourceClient`, `OciSourceClient`, `AzureAcrAuthWinCheck` each declare `private static readonly HttpClient Http = new()`. Works fine for a desktop app but prevents configuring timeouts or injecting handlers per-instance.

---

### `CombinedContainerPlatformRunner` hardcoded to two runners

Constructor takes exactly `_docker` and `_podman`. Adding a third runtime requires restructuring.

---

### Docker vs Podman output format differences

Important context for container runner work:
- **JSON**: Docker outputs newline-delimited objects; Podman outputs a JSON array.
- **Labels**: Docker returns comma-separated string; Podman returns JSON object.
- **ID casing**: Docker `"ID"` (uppercase); Podman `"Id"` (PascalCase).

---

### Doctor.Gui uses `DependencyObject`, Gui uses `INotifyPropertyChanged`

Both are valid WPF patterns but inconsistent. `INotifyPropertyChanged` is more portable if WinUI migration ever happens.

---

### `MainWindow.xaml.cs` (Crosspose.Gui) is very large

View model classes (`ContainerRow`, `ProjectGroupRow`, `ImageRow`, `VolumeRow`, `DeploymentRow`, `ChartFileRow`, `ProjectEntry`) are defined inline. These should move to a `ViewModels/` folder as the file grows.

---

### `WslToWindowsFirewallCheck` timing causes false negatives

`WslToWindowsFirewallCheck` checks whether reverse port proxy rules exist on the WSL vEthernet interface, but `PortProxyApplicator` may not have run yet when the check executes. This can cause the check to report failure even when the rules will be applied correctly on the next `crosspose up`. The fix is to either defer the check until after orchestration completes or re-query proxy state at check time.

**File**: `src/Crosspose.Doctor/Checks/WslToWindowsFirewallCheck.cs`

---

### Duplicate firewall rules accumulate

`PortProxyCheck.FixAsync` adds `netsh advfirewall firewall add rule` on every fix run but never checks if the rule already exists. Firewall `add` silently creates duplicates (unlike portproxy `add` which updates). Over time, hundreds of identical rules accumulate. The fix should check for existing rules by name before adding, or delete-then-add.

**File**: `src/Crosspose.Doctor/Checks/PortProxyCheck.cs:119-134`

