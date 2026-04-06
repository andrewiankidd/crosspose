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

### Linux→Windows container communication (reverse NAT bridging)

**Status**: Partially implemented. Windows→Linux works via `NAT_GATEWAY_IP` + `netsh portproxy`. Linux→Windows requires a mirror mechanism that doesn't exist yet.

**Problem**: Docker Desktop binds Windows container ports to `127.0.0.1` only (not `0.0.0.0`), so WSL/Podman containers can't reach them directly. Additionally, the Windows firewall may block inbound from the WSL virtual interface.

**Proven working path** (tested 2026-04-06 on Machine B):
1. Resolve the WSL-facing interface IP on the Windows host (the `vEthernet (WSL*)` adapter, e.g. `172.24.112.1`).
2. Add a `netsh interface portproxy` rule: `listenaddress=<WSL_HOST_IP> listenport=<service-port> connectaddress=127.0.0.1 connectport=<docker-mapped-port>`.
3. Add a Windows firewall inbound rule: `netsh advfirewall firewall add rule name=... dir=in action=allow protocol=TCP localip=<WSL_HOST_IP> localport=<service-port>`.
4. On Windows 11 with Hyper-V firewall: may also need `New-NetFirewallHyperVRule` for the WSL VM creator ID (`{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}`).
5. Linux compose env vars use `WSL_HOST_IP` (analogous to `NAT_GATEWAY_IP`) for cross-OS Windows service references.
6. The orchestrator must pass `WSL_HOST_IP` to Podman compose via the `env` prefix in the WSL command (Windows env vars don't propagate into WSL automatically — see `PodmanContainerRunner.ExecAsync`).

**Implementation plan**:
- Add `WSL_HOST_IP` resolution to `ComposeOrchestrator.BuildEnvironmentAsync` (resolve from `vEthernet (WSL*)` adapter).
- Add reverse port proxy requirements to `conversion-report.yaml` for Windows services referenced by Linux services.
- Doctor check: detect WSL→Windows connectivity (try TCP to `WSL_HOST_IP:3389` or similar) and fix by adding firewall rules.
- `PortProxyApplicator`: handle reverse-direction rules on the WSL interface in addition to NAT gateway rules.

**Related files**: `ComposeOrchestrator.cs`, `ComposeGenerator.cs` (RemapServiceUrls), `PortProxyApplicator.cs`, `PortProxyCheck.cs`, `PodmanContainerRunner.cs` (env var injection).

---

### Duplicate firewall rules accumulate

`PortProxyCheck.FixAsync` adds `netsh advfirewall firewall add rule` on every fix run but never checks if the rule already exists. Firewall `add` silently creates duplicates (unlike portproxy `add` which updates). Over time, hundreds of identical rules accumulate. The fix should check for existing rules by name before adding, or delete-then-add.

**File**: `src/Crosspose.Doctor/Checks/PortProxyCheck.cs:119-134`

---

### `StalePortProxyCheck` and `PortProxyApplicator` must query the correct WSL distro

Previously both used `wsl -- ss -tlnp` which queries the DEFAULT WSL distro (e.g. Ubuntu), not `crosspose-data` where podman runs. This caused valid port proxy rules to be incorrectly identified as stale and deleted. Fixed 2026-04-06 to use `wsl -d {CrossposeEnvironment.WslDistro}` and fall back to `netstat -tlnp` since Alpine doesn't ship `ss`.

The `netstat` output format differs from `ss` — column indices for local address are different. Parser now uses flexible token matching instead of fixed column index.

**Files**: `src/Crosspose.Doctor/Checks/StalePortProxyCheck.cs`, `src/Crosspose.Core/Networking/PortProxyApplicator.cs`

---

### App service host port range conflicts with Windows dynamic ports

`ComposeGenerator.GetNextHostPort()` previously allocated from 60000-65000, which overlaps with the Windows dynamic port range (49152-65535). Ports in this range may be reserved by Windows and unavailable for WSL port forwarding. Fixed 2026-04-06 to use 30000-39999. Infra ports use 40000-49999 (already safe).

**File**: `src/Crosspose.Dekompose/Services/ComposeGenerator.cs:1044`
