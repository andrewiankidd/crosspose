# Tech Debt

Structural issues worth knowing about when working in the codebase. This is a PoC — these are cleanup items, not urgent problems.

---

### 18 bare catch blocks across the codebase

Files: `ComposeOrchestrator.cs`, `AppDataLocator.cs`, `CrossposeConfigurationStore.cs`, `DeploymentMetadataStore.cs`, `ComposeProjectLoader.cs`, `WindowsNatUtilities.cs`, `OciSourceClient.cs`, `SourceNameGenerator.cs`, `Program.cs` (Cli + Dekompose.Cli), `MainWindow.xaml.cs` (Gui + Dekompose.Gui), `AddRepoWindow.xaml.cs`, `App.xaml.cs`.

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

### No test infrastructure

Zero test projects. Key testable units: container runner JSON parsing, Doctor check logic, ComposeGenerator output, ComposeOrchestrator routing.

---

### Doctor.Gui uses `DependencyObject`, Gui uses `INotifyPropertyChanged`

Both are valid WPF patterns but inconsistent. `INotifyPropertyChanged` is more portable if WinUI migration ever happens.
