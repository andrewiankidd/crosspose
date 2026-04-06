# CLAUDE.md — Crosspose.Ui

See also: [root CLAUDE.md](../../CLAUDE.md)

## Purpose

Shared WPF component library used by all GUI projects. Contains reusable windows and controls that would otherwise be duplicated across `Crosspose.Gui`, `Crosspose.Dekompose.Gui`, and `Crosspose.Doctor.Gui`.

## Contents

- **`LogViewerControl`** — reusable log display control (XAML + code-behind).
- **`AddChartSourceWindow`** — dialog to add a Helm repo or OCI registry as a chart source. Handles Azure ACR auth detection and bearer token/credential flows.
- **`PickChartWindow`** — dialog to browse chart sources, select a chart/version, and pull a tgz to the local helm-charts directory.
- **`ChartSourceListItem`** — view model for chart source entries (Name, Url, IsOci, Username, Password, BearerToken, Filter).
- **`DoctorCheckPersistence`** — static helper that calls `DoctorCheckRegistrar.EnsureChecks` to persist additional Doctor checks to `crosspose.yml`.

## Dependencies

- `Crosspose.Core` — for `HelmClient`, `CrossposeEnvironment`, configuration, logging.
- `Crosspose.Doctor.Core` — for `DoctorCheckRegistrar`.
- `net10.0-windows10.0.19041`, `UseWPF=true`.
