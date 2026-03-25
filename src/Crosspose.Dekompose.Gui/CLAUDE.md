# CLAUDE.md — Crosspose.Dekompose.Gui

See also: [root CLAUDE.md](../../CLAUDE.md) | [Dekompose library](../Crosspose.Dekompose/CLAUDE.md)

## Purpose

WPF GUI for Dekompose. Lets users select a Helm chart (local or from a configured repo/OCI source), pick values, and run conversion. Manages chart sources via an Add Repo dialog. Runs Doctor checks inline before conversion.

## Windows

- **MainWindow** — chart source selection, values file picker, conversion execution.
- **AddRepoWindow** — dialog to add Helm repo or OCI registry as a chart source.
- **AboutWindow** — version info.

## Dependencies

- `Crosspose.Core`, `Crosspose.Dekompose`, `Crosspose.Doctor`, `Crosspose.Ui`
- Dark/light theme support via `Themes/Colors.Dark.xaml` and `Colors.Light.xaml`.
