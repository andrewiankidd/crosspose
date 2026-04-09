# Recommendations

Prioritized action plan.

---

## Quick Fixes

### Add logging to critical bare catch blocks
The `ComposeOrchestrator` and `ComposeProjectLoader` bare catches should log at Debug level at minimum, since failures in compose file parsing are actionable.

### Individual container start should force-recreate Podman containers
`crosspose container start <name>` currently calls `combined.StartContainerAsync` which maps to `podman start` — the same stale-network-namespace problem as `restart`. Ideally this should detect the platform and issue `podman-compose up --force-recreate` for Podman containers instead.

---

## Clean Up Before Expanding

### Add `ConfigureAwait(false)` to Doctor.Core and Dekompose.Core
Mechanical, zero-risk. Add `.ConfigureAwait(false)` to all `await` calls in `AzureAcrAuthLinCheck`, `AzureAcrAuthWinCheck`, `AzureCliCheck`, `CrossposeWslCheck`, `ComposeGenerator`, `HelmTemplateRunner`. See tech-debt.md for full file list.

### Fix `Process.Start()` in `AzureAcrAuthWinCheck`
Small, surgical. Replace the direct `Process.Start()` at line 139 with `await runner.RunAsync(...)` using the `ProcessRunner` already passed to `FixAsync`. Brings this check into line with every other check.

### Move `ParseImageRef` and compose file patching to Core
`ContainerDetailsWindow` contains image-string parsing and compose file I/O logic. Move `ParseImageRef` to `Crosspose.Core.Orchestration` and the file patching to `Crosspose.Core.Deployment`. Also switch from synchronous to async file I/O. See tech-debt.md for details.

---

## Clean Up Before Expanding (existing items)

### Extract view models from MainWindow.xaml.cs (Crosspose.Gui)
The main GUI's `MainWindow.xaml.cs` is very large. View models (`ContainerRow`, `ProjectGroupRow`, `ImageRow`, `VolumeRow`, `DeploymentRow`, `ChartFileRow`, `ProjectEntry`) should move to a `ViewModels/` folder.

### Dispose `JsonDocument` instances in container runners
Refactor Docker's `EnumerateJsonElements` to avoid `yield return` with `JsonDocument`. Add `using` to Podman's `JsonDocument.Parse` calls.

### Cache deployment directory enumeration in MainWindow refresh
`EnumerateDeploymentRows` in `MainWindow.xaml.cs` reads the filesystem (directory enumeration + metadata file reads) on every timer tick. For small deployment counts this is fine, but with many deployments it grows linearly. Cache the deployment list and invalidate only after a deploy/remove/up/down action rather than on every refresh cycle.

**File**: `src/Crosspose.Gui/MainWindow.xaml.cs` — `EnumerateDeploymentRows` called from timer tick via `RefreshCurrentViewAsync`.

---

### Eliminate redundant container fetch in GUI refresh
`RefreshContainersInternal` fires both `GetContainersDetailedAsync` (JSON) and `GetContainersAsync` (raw text) in parallel. The raw result is only used for fallback. Call JSON first, fall back to raw only when needed.

---

## Feature Roadmap

### CI pipeline
`dotnet build` + `dotnet test` — tests now exist (~170 across Core, Doctor, Dekompose).

---

## Future Considerations

### N-runtime support
Refactor `CombinedContainerPlatformRunner` to accept `IReadOnlyList<IContainerPlatformRunner>` if containerd/nerdctl support is needed.

### HttpClient management
The 5 static `HttpClient` instances work fine for a desktop app. If the tool becomes a long-running service, consider `IHttpClientFactory`.

### Standardize MVVM
Pick one approach (`INotifyPropertyChanged` recommended) if extracting shared view model patterns across GUI projects.
