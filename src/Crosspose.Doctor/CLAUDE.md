# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this project.

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Prerequisite check library (like `flutter doctor`). Contains the check/fix interface, all check implementations, and the check catalog. Used by both `Crosspose.Doctor.Cli` and `Crosspose.Doctor.Gui`.

## ICheckFix Interface

All checks implement `ICheckFix` (`Checks/ICheckFix.cs`):
- `Name` — display name for the check
- `CanFix` — whether automated remediation is available
- `RunAsync(ProcessRunner, ILogger, CancellationToken)` → `CheckResult`
- `FixAsync(ProcessRunner, ILogger, CancellationToken)` → `FixResult`

## Check Catalog

`CheckCatalog.LoadAll()` returns checks in execution order:
1. **DockerComposeCheck** — tries `docker compose version`, falls back to `docker-compose --version`. Fix: `winget install Docker.DockerDesktop`.
2. **WslCheck** — runs `wsl --status`. Fix: `wsl --install`.
3. **CrossposeWslCheck** — verifies a dedicated `crosspose-data` Alpine WSL distro exists. Fix: exports Alpine, imports as `crosspose-data`, creates a user.
4. **HelmCheck** — runs `helm version --short`. Fix: `winget install Helm.Helm`.

## Adding a New Check

1. Create a class implementing `ICheckFix` in `Checks/`.
2. Add it to the array in `CheckCatalog.LoadAll()`.
3. Both Doctor.Cli and Doctor.Gui will automatically pick it up.

## Dependencies

- `Crosspose.Core` — for `ProcessRunner` and logging.
