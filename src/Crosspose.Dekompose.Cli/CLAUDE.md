# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this project.

See also: [root CLAUDE.md](../../CLAUDE.md) | [Dekompose library CLAUDE.md](../Crosspose.Dekompose/CLAUDE.md)

## Purpose

Thin CLI entry point for Crosspose Dekompose. Parses args (`--chart`, `--values`, `--manifest`, `--output`), invokes `HelmTemplateRunner` and `ComposeStubWriter`.

## Build and Run

```powershell
# Render via helm
dotnet run --project src/Crosspose.Dekompose.Cli -- --chart C:\path\to\chart --values C:\path\to\values.yaml

# Use a pre-rendered manifest
dotnet run --project src/Crosspose.Dekompose.Cli -- --manifest C:\path\to\manifest.yaml --output C:\temp\out
```

## Dependencies

- `Crosspose.Core` — for `ProcessRunner`, `ShellDetection`, and logging.
- `Crosspose.Dekompose` — for `HelmTemplateRunner` and `ComposeStubWriter`.
