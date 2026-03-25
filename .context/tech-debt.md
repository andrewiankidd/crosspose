# Tech Debt

Structural issues worth knowing about when working in the codebase. This is a PoC — these are things to address as the project solidifies, not urgent problems.

---

### `ProcessRunner` log helpers create tight coupling

`ProcessRunner` has `internal` log wrapper methods (`LogDebug`, `LogWarning`, etc.) that container runners call via `Runner.LogDebug(...)`. This means runners depend on `ProcessRunner` for both execution *and* logging, making them harder to test in isolation.

---

### `JsonDocument` instances not disposed in container runners

`JsonDocument.Parse()` returns a disposable object holding pooled memory. Docker's `EnumerateJsonElements` uses `yield return` which prevents disposal. Podman's parsing also skips `using`. With the GUI refreshing every 5 seconds, this creates steady memory pressure until GC finalizes.

**Files**: `DockerContainerRunner.cs:155-169`, `PodmanContainerRunner.cs:62,118,148`

---

### GUI fires redundant parallel requests on every container refresh

`RefreshContainersInternal` runs both `GetContainersDetailedAsync` (JSON, structured) and `GetContainersAsync` (raw text) in parallel. The raw text result is only used for fallback table parsing when JSON returns nothing. In the common case, this doubles docker/podman process spawning every refresh cycle.

**File**: `src/Crosspose.Gui/MainWindow.xaml.cs:104-105`

---

### View models inline in MainWindow.xaml.cs

`ContainerRow`, `ProjectGroupRow`, `ImageRow`, `VolumeRow` are defined at the bottom of the 640-line `MainWindow.xaml.cs`. Extracting to a `ViewModels/` folder would improve navigability.

---

### Docker vs Podman output format differences

Not a bug, but important context for anyone modifying the container runners:
- **JSON structure**: Docker outputs newline-delimited JSON objects; Podman outputs a JSON array. `DockerContainerRunner.EnumerateJsonElements` handles both; `PodmanContainerRunner` assumes array only.
- **Labels**: Docker returns labels as a comma-separated string (`"key=val,key2=val2"`). Podman returns a JSON object (`{"key":"val"}`). Each runner has different parsing logic.
- **ID field casing**: Docker uses `"ID"` (uppercase); Podman uses `"Id"` (PascalCase).
