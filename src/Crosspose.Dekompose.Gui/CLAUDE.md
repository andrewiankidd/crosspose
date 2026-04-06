# CLAUDE.md — Crosspose.Dekompose.Gui

See also: [root CLAUDE.md](../../CLAUDE.md) | [Dekompose library](../Crosspose.Dekompose/CLAUDE.md)

## Purpose

WPF GUI for Dekompose. Lets users select a Helm chart (local or from a configured repo/OCI source), pick values, and run conversion. Manages chart sources via a shared Add Chart Source dialog from `Crosspose.Ui`. Runs Doctor checks inline before conversion.

## Arguments

- `--chart <path>` / `-c <path>` — open with a pre-supplied chart tgz. When provided, source browsing rows are hidden and the chart field is pre-populated.

## Windows

- **MainWindow** — chart source selection, values file picker, conversion execution.
- **AboutWindow** — version info.
- **AddChartSourceWindow** (from `Crosspose.Ui`) — dialog to add Helm repo or OCI registry as a chart source.

## Dependencies

- `Crosspose.Core`, `Crosspose.Dekompose`, `Crosspose.Doctor`, `Crosspose.Ui`
- Dark/light theme support via `Themes/Colors.Dark.xaml` and `Colors.Light.xaml`.
