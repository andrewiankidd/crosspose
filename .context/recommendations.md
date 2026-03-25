# Recommendations

Prioritized action plan. This is a PoC — focus is on getting core functionality working, then hardening.

---

## Clean Up Before Expanding

Address before adding major new features, to avoid compounding the issues.

### Extract view models from MainWindow.xaml.cs
Move `ContainerRow`, `ProjectGroupRow`, `ImageRow`, `VolumeRow` to a `ViewModels/` folder. Shrinks the 640-line file and makes the models reusable.

### Eliminate redundant container fetch
Only call `GetContainersDetailedAsync` on each refresh. Fall back to `GetContainersAsync` raw text parsing only when JSON returns zero results. Halves process spawning on the 5-second refresh cycle.

### Dispose `JsonDocument` instances
Refactor Docker's `EnumerateJsonElements` to avoid `yield return` (which prevents disposal). Add `using` to Podman's `JsonDocument.Parse` calls.

---

## Feature Roadmap

The core PoC porting work, in dependency order.

### 1. Compose file generation (Dekompose)
This is the main reason the rewrite exists. Port from `C:\git\crossposeps\src\Main.ps1`:

1. Add `YamlDotNet` NuGet to Dekompose.
2. Create `ManifestParser` — read multi-document YAML, extract workload definitions (Deployments, StatefulSets, Services).
3. Create `ComposeEmitter` — map K8s specs to compose service definitions.
4. Port the port assignment algorithm (avoid conflicts between workloads).
5. Translate ConfigMaps → env files or bind mounts, Secrets → env vars with TODO placeholders.
6. Emit per-workload compose files: `docker-compose.<workload>.windows.yml`, `docker-compose.<workload>.linux.yml`.
7. Emit shared resources file (networks, volumes).
8. Replace `ComposeStubWriter` call in `Program.cs`.

Reference output: `C:\git\crossposeps\docker-compose-outputs\`

### 2. Compose orchestration (CLI)
Depends on compose file generation. Port from `C:\git\crossposeps\assets\scripts\compose.ps1`:

1. Create `ComposeOrchestrator` in Core with `Start`, `Stop`, `Restart`, `Status`, `Validate`.
2. Route Windows compose files to `docker compose`, Linux files to `wsl podman compose`.
3. Handle path translation (Windows ↔ WSL), network driver fixes, ACR auth checks.
4. Wire into Cli's `compose` command, replacing the stub.
5. Reuse from the GUI for orchestration buttons.

### 3. Container detail tabs (GUI)
Implement `ContainerDetailsWindow` tabs in priority order:

1. **Logs** — stream `docker/podman logs -f` via `ProcessRunner` with `OutputHandler`.
2. **Inspect** — one-shot `docker/podman inspect` formatted as JSON.
3. **Stats** — streaming `docker/podman stats --no-stream` on a timer.
4. **Bind mounts** — extract from inspect output.
5. **Files** / **Exec** — defer (exec needs pseudo-terminal support).

### 4. Test infrastructure
Create `src/Crosspose.Core.Tests/` with xUnit. Priority targets:
- Container runner JSON parsing (both formats, label extraction, malformed input)
- Doctor check logic (mock `ProcessRunner` with predefined results)
- `CombinedContainerPlatformRunner.Merge` error aggregation
- Compose generation output (once implemented) tested against prototype sample output

### 5. CI pipeline
Once tests exist:
- `dotnet build Crosspose.sln`
- `dotnet test`
- Consider `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` via `Directory.Build.props`

---

## Future Considerations

Decisions that don't need to be made yet but will come up.

### WinUI 3 migration
`Crosspose.Gui.csproj` already has `<UseWinUI>false</UseWinUI>` as a breadcrumb. Business logic is in Core, so the GUI should stay thin. If/when this happens, standardize on `INotifyPropertyChanged` across both GUI projects (Doctor.Gui currently uses `DependencyObject`, Gui uses INPC).

### System.CommandLine for CLI parsing
The manual `Queue<string>` parsing works fine for the current scope. If the CLIs grow significantly, `System.CommandLine` provides auto-generated help, type safety, and tab completion. Start with Dekompose (most complex args).

### N-runtime support in CombinedContainerPlatformRunner
Currently hardcoded to docker + podman. If containerd/nerdctl support is needed, refactor to accept `IReadOnlyList<IContainerPlatformRunner>`. The fan-out/merge pattern already generalizes.

### PodmanContainerRunner WSL mode
The `runInsideWsl` parameter exists but is always `false`. For systems where podman only lives inside WSL, this would need to be `true` — but the code path is untested. Wire up when there's a real use case.
