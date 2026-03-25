# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this project.

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Helm-to-Compose conversion library. Contains `HelmTemplateRunner`, `ComposeStubWriter`, and will contain the future compose generation pipeline. Used by `Crosspose.Dekompose.Cli` and eventually by `Crosspose.Gui`.

## Current State

- `HelmTemplateRunner` shells out to `helm template` and writes rendered YAML to the output directory.
- `ComposeStubWriter` copies the manifest and writes a `TODO.compose-generation.md` placeholder. Actual compose generation is not yet ported.
- Output goes to `./docker-compose-outputs/` by default (gitignored).

## Porting Targets

The compose generation logic needs to be ported from `C:\git\crossposeps`:
- Workload/OS detection and port assignment from `src/Main.ps1`
- ConfigMap/Secret → bind mount translation
- Expected output pattern: `docker-compose.<workload>.<os>.yml` (see `crossposeps/docker-compose-outputs/`)

## Dependencies

- `Crosspose.Core` — for `ProcessRunner` and logging.
