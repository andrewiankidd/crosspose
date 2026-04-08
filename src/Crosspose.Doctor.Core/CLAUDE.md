# CLAUDE.md — Crosspose.Doctor.Core

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Prerequisite check library. Contains the `ICheckFix` interface, all check implementations, `CheckCatalog`, and `DoctorSettings`. Used by `Crosspose.Doctor.Cli`, `Crosspose.Doctor.Gui`, and `Crosspose.Cli` (for pre-compose validation).

## ICheckFix Interface

```csharp
string Name { get; }
string Description { get; }
bool IsAdditional { get; }
string AdditionalKey { get; }
bool CanFix { get; }
bool RequiresConnectivity { get; }   // default: false — override to true for Azure/network checks
bool AutoFix { get; }                // default: false — when true, DoctorMonitor fixes automatically
int CheckIntervalSeconds { get; }    // default: 60 — background re-check interval
Task<CheckResult> RunAsync(ProcessRunner, ILogger, CancellationToken);
Task<FixResult> FixAsync(ProcessRunner, ILogger, CancellationToken);
```

## Built-in Checks (CheckCatalog order)

DockerCompose, DockerRunning, DockerWindowsMode, HnsNatHealth, OrphanedDockerNetwork, OrphanedPodmanNetwork (AutoFix), StalePortProxyConfig, WSL, WslMemoryLimit, WslNetworkingMode, StalePortProxy, StaleFirewallRule (AutoFix), Sudo, CrossposeWsl, PodmanWsl, PodmanCgroup, PodmanComposeWsl, Helm, AzureCli (RequiresConnectivity), PodmanHealthcheckRunner (AutoFix), PodmanCreatedContainer (AutoFix), PodmanContainerAutoheal (AutoFix), WslToWindowsFirewall (AutoFix).

`WslCheck` uses internal cancellation timeouts and a 3-stage fix: `wsl --shutdown` (15s) → `net stop WslService/LxssManager` (15s) → `taskkill wslservice.exe/wsl.exe` (10s) → `wsl --install`.

## Additional Checks (enabled via config or `--enable-additional`)

- `azure-acr-auth-win:<registry>` / `azure-acr-auth-lin:<registry>` — ACR auth for Windows/Linux (RequiresConnectivity, AutoFix).
- `port-proxy:<listenPort>:<connectPort>@<network>` — Windows `netsh` port proxy for Docker↔WSL bridging (AutoFix).

## Offline Mode

`CheckCatalog.LoadAll(offlineMode: true)` filters out all checks where `RequiresConnectivity == true`. Persisted to `crosspose.yml` as `offline-mode`.

## Adding a New Check

1. Implement `ICheckFix` in `Checks/`.
2. Add to the list in `CheckCatalog.LoadAll()`.
3. If it's an additional check, set `IsAdditional = true` and provide an `AdditionalKey`.
4. If it needs network/cloud, add `public bool RequiresConnectivity => true;`.
5. If DoctorMonitor should auto-fix it, add `public bool AutoFix => true;`.

## Dependencies

- `Crosspose.Core` — for `ProcessRunner`, `CrossposeEnvironment`, `AppDataLocator`, `WslHostResolver`.
- `YamlDotNet`, `Tomlyn` — for config/manifest parsing.
