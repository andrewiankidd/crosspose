# CLAUDE.md — Crosspose.Doctor

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
Task<CheckResult> RunAsync(ProcessRunner, ILogger, CancellationToken);
Task<FixResult> FixAsync(ProcessRunner, ILogger, CancellationToken);
```

## Built-in Checks (CheckCatalog order)

DockerCompose, DockerRunning, DockerWindowsMode, HnsNatHealth, OrphanedDockerNetwork, StalePortProxyConfig, WSL, WslMemoryLimit, WslNetworkingMode, StalePortProxy, Sudo, CrossposeWsl, PodmanWsl, PodmanCgroup, PodmanComposeWsl, Helm, AzureCli (RequiresConnectivity).

## Additional Checks (enabled via config or `--enable-additional`)

- `azure-acr-auth-win:<registry>` / `azure-acr-auth-lin:<registry>` — ACR auth for Windows/Linux (RequiresConnectivity).
- `port-proxy:<listenPort>:<connectPort>@<network>` — Windows `netsh` port proxy for Docker↔WSL bridging.

## Offline Mode

`CheckCatalog.LoadAll(offlineMode: true)` filters out all checks where `RequiresConnectivity == true`. Persisted to `crosspose.yml` as `offline-mode`.

## Adding a New Check

1. Implement `ICheckFix` in `Checks/`.
2. Add to the list in `CheckCatalog.LoadAll()`.
3. If it's an additional check, set `IsAdditional = true` and provide an `AdditionalKey`.
4. If it needs network/cloud, add `public bool RequiresConnectivity => true;`.

## Dependencies

- `Crosspose.Core` — for `ProcessRunner`, `CrossposeEnvironment`, `AppDataLocator`.
- `YamlDotNet`, `Tomlyn` — for config/manifest parsing.
