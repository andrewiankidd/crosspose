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

### Extract view models from MainWindow.xaml.cs (Crosspose.Gui)
The main GUI's `MainWindow.xaml.cs` is very large. View models (`ContainerRow`, `ProjectGroupRow`, `ImageRow`, `VolumeRow`, `DeploymentRow`, `ChartFileRow`, `ProjectEntry`) should move to a `ViewModels/` folder.

### Dispose `JsonDocument` instances in container runners
Refactor Docker's `EnumerateJsonElements` to avoid `yield return` with `JsonDocument`. Add `using` to Podman's `JsonDocument.Parse` calls.

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
