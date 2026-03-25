# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this project.

See also: [root CLAUDE.md](../../CLAUDE.md) | [Doctor library CLAUDE.md](../Crosspose.Doctor/CLAUDE.md)

## Purpose

Thin CLI entry point for Crosspose Doctor. Parses `--fix`/`--help` args, iterates checks from `CheckCatalog`, and prints results.

## Build and Run

```powershell
dotnet run --project src/Crosspose.Doctor.Cli             # Check only
dotnet run --project src/Crosspose.Doctor.Cli -- --fix     # Check + attempt fixes
```

## Dependencies

- `Crosspose.Core` — for `ProcessRunner`, `ShellDetection`, and logging.
- `Crosspose.Doctor` — for `CheckCatalog` and `ICheckFix` implementations.
