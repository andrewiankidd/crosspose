# CLAUDE.md — Crosspose.Doctor.Cli

See also: [root CLAUDE.md](../../CLAUDE.md) | [Doctor library](../Crosspose.Doctor.Core/CLAUDE.md)

## Purpose

Thin CLI entry point for Doctor. Iterates checks from `CheckCatalog`, prints results, optionally applies fixes. Persists additional check state to `DoctorSettings`.

## Arguments

- `--fix`, `-f` — attempt automated fixes for failed checks.
- `--enable-additional <key>` — enable an additional check (e.g., `azure-acr-auth-win:myregistry.azurecr.io`).
- `--help`, `--version`

## Dependencies

- `Crosspose.Core`, `Crosspose.Doctor.Core`
