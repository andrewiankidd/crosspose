# Crosspose.Ui

Shared WPF component library used by all GUI projects (`Crosspose.Gui`, `Crosspose.Dekompose.Gui`, `Crosspose.Doctor.Gui`).

## Components

### LogViewerControl
Scrolling log panel that tails the in-memory log store from `Crosspose.Core.Diagnostics`. Used in every GUI for diagnostics and support capture.

### AddChartSourceWindow
Modal dialog for adding Helm repository or OCI registry sources. Writes new sources to `crosspose.yml` via `OciSourceClient` and triggers `helm repo add`.

### PickChartWindow
Chart browser that lists available charts and versions from configured sources. Used by Crosspose.Gui (Charts view) and Crosspose.Dekompose.Gui (repo flow) to select a chart tgz for download or conversion.

### ChartSourceListItem
View model and display item for chart sources in the picker and source management UIs.

### DoctorCheckPersistence
Helper that writes new `doctor.additional-checks` entries (ACR auth, port proxy) into `crosspose.yml` when Dekompose or a GUI detects a new infrastructure requirement.

## Dependencies
- `Crosspose.Core` — configuration, logging, source clients, process runner.
- `Crosspose.Doctor` — check catalogue (used by `DoctorCheckPersistence`).

## Related docs
- [Crosspose.Gui](../crosspose.gui/README.md)
- [Crosspose.Dekompose.Gui](../crosspose.dekompose.gui/README.md)
- [Crosspose.Doctor.Gui](../crosspose.doctor.gui/README.md)
