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
Task<CheckResult> RunAsync(ProcessRunner, ILogger, CancellationToken);
Task<FixResult> FixAsync(ProcessRunner, ILogger, CancellationToken);
```

## Built-in Checks (CheckCatalog)

DockerCompose, DockerRunning, DockerWindowsMode, WSL, WslMemoryLimit, Sudo, CrossposeWsl, PodmanWsl, PodmanCgroup, PodmanComposeWsl, Helm, AzureCli.

## Additional Checks (enabled via config or `--enable-additional`)

- `azure-acr-auth-win:<registry>` / `azure-acr-auth-lin:<registry>` — ACR auth for Windows/Linux.
- `port-proxy:<port>@<network>` — Windows `netsh` port proxy for Docker↔WSL bridging.

## Adding a New Check

1. Implement `ICheckFix` in `Checks/`.
2. Add to the list in `CheckCatalog.LoadAll()`.
3. If it's an additional check, set `IsAdditional = true` and provide an `AdditionalKey`.

## Dependencies

- `Crosspose.Core` — for `ProcessRunner`, `CrossposeEnvironment`, `AppDataLocator`.
- `YamlDotNet`, `Tomlyn` — for config/manifest parsing.
