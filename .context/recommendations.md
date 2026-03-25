# Recommendations

Prioritized action plan.

---

## Quick Fixes

### Replace Debug.WriteLine with proper logging
4 locations use `System.Diagnostics.Debug.WriteLine` which is invisible in Release builds:
- `PodmanContainerRunner.cs:127` — use `Runner.LogWarning`
- `OciSourceClient.cs:152` — use logger
- `FixWindow.xaml.cs:93` — acceptable (clipboard copy failure)
- `LogViewerControl.xaml.cs:82` — acceptable (clipboard copy failure)

### Add logging to critical bare catch blocks
The `ComposeOrchestrator` and `ComposeProjectLoader` bare catches should log at Debug level at minimum, since failures in compose file parsing are actionable.

---

## Clean Up Before Expanding

### Extract view models from MainWindow.xaml.cs (Crosspose.Gui)
The main GUI's `MainWindow.xaml.cs` is the largest file. View models (`ContainerRow`, `ProjectGroupRow`, `ImageRow`, `VolumeRow`, `DeploymentRow`, `ProjectEntry`) should move to a `ViewModels/` folder.

### Dispose `JsonDocument` instances in container runners
Refactor Docker's `EnumerateJsonElements` to avoid `yield return` with `JsonDocument`. Add `using` to Podman's `JsonDocument.Parse` calls.

### Eliminate redundant container fetch in GUI refresh
`RefreshContainersInternal` fires both `GetContainersDetailedAsync` (JSON) and `GetContainersAsync` (raw text) in parallel. The raw result is only used for fallback. Call JSON first, fall back to raw only when needed.

---

## Feature Roadmap

### Test infrastructure
Create `src/Crosspose.Core.Tests/` with xUnit. Priority targets:
- Container runner JSON parsing (both Docker/Podman formats)
- Doctor check logic (mock `ProcessRunner`)
- `ComposeGenerator` output validation
- `ComposeOrchestrator` routing logic

### CI pipeline
Once tests exist: `dotnet build` + `dotnet test`.

---

## Future Considerations

### N-runtime support
Refactor `CombinedContainerPlatformRunner` to accept `IReadOnlyList<IContainerPlatformRunner>` if containerd/nerdctl support is needed.

### HttpClient management
The 5 static `HttpClient` instances work fine for a desktop app. If the tool becomes a long-running service, consider `IHttpClientFactory`.

### Standardize MVVM
Pick one approach (`INotifyPropertyChanged` recommended) if extracting shared view model patterns across GUI projects.
