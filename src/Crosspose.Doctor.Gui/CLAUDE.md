# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this project.

See also: [root CLAUDE.md](../../CLAUDE.md) | [Doctor CLAUDE.md](../Crosspose.Doctor/CLAUDE.md)

## Purpose

WPF front-end for Crosspose Doctor. Shows each prerequisite check with status and a per-item Fix button.

## Build and Run

```powershell
dotnet run --project src/Crosspose.Doctor.Gui
```

## Architecture

- `MainWindow` loads checks via `CheckCatalog.LoadAll()` on `Loaded` event, runs each check, and populates an `ObservableCollection<CheckViewModel>`.
- `CheckViewModel` uses WPF `DependencyProperty` for data binding (`Name`, `Result`, `IsSuccess`, `IsFixEnabled`).
- Fix button opens a `FixWindow` dialog that runs `ICheckFix.FixAsync()` and reports the result.
- Targets `net10.0-windows10.0.19041` with WPF (`UseWPF=true`).

## Dependencies

- `Crosspose.Core` — for `ProcessRunner` and logging.
- `Crosspose.Doctor` — for `CheckCatalog` and `ICheckFix` implementations.
