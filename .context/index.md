# Crosspose — LLM Context Index

This directory contains deep context files about the Crosspose codebase, designed for consumption by LLMs assisting with development. Each file is self-contained and focused on a specific dimension of the system.

## Files

| File | What it covers |
|------|----------------|
| [overview.md](overview.md) | Project purpose, status, and relationship to the PowerShell prototype |
| [architecture.md](architecture.md) | Solution structure, project dependency graph, namespace map |
| [type-catalog.md](type-catalog.md) | Every public type with signatures, inheritance, and usage notes |
| [data-flow.md](data-flow.md) | How data moves through the system: process execution, container enumeration, helm rendering, doctor checks |
| [gui-internals.md](gui-internals.md) | WPF architecture: windows, view models, converters, data binding, refresh loop |
| [cli-contracts.md](cli-contracts.md) | CLI argument parsing, exit codes, output formats for all three CLIs |
| [extension-points.md](extension-points.md) | How to add new checks, container runners, views, and conversion pipeline stages |
| [conventions.md](conventions.md) | Code patterns, naming, async usage, error handling |
| [external-tools.md](external-tools.md) | Every external CLI tool invoked, with exact arguments and expected output formats |
| [bugs.md](bugs.md) | Confirmed broken behavior with root cause and fix guidance |
| [tech-debt.md](tech-debt.md) | Structural issues to be aware of when working in the codebase |
| [recommendations.md](recommendations.md) | Prioritized action plan: fixes, cleanup, feature roadmap, future considerations |
