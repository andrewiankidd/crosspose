# Crosspose.Doctor.Gui

## Overview
Crosspose.Doctor.Gui is a WPF shell around the prerequisite checks exposed by `Crosspose.Doctor`. It runs the same checks and fixes as the CLI, but with an interactive UI for status, remediation, and logs.

## Checks dashboard
The dashboard runs the full suite of checks on load and shows:
- Name and description of the requirement.
- Pass/Fail/Warning result.
- Fix button when remediation is available.
- **Fix All** button to run all pending fixes in sequence.

When offline mode is active, an amber banner is shown at the top and connectivity-requiring checks (Azure CLI, ACR auth) are omitted.

## Fix workflow
Fix opens a modal that streams output from the underlying command (winget install, WSL provisioning, `netsh interface portproxy`, etc.). The dialog stays locked until the command completes so you can re-run checks with a stable baseline. **Fix All** opens `FixAllWindow`, which runs each fixable failed check in sequence with combined output.

## Additional checks
Doctor.Gui reads `doctor.additional-checks` from `crosspose.yml`, which includes:
- `azure-acr-auth-win:<registry>`
- `azure-acr-auth-lin:<registry>`
- `port-proxy:<listenPort>:<connectPort>@<network>`

These are added automatically by Dekompose and the GUIs as needed.

## Log viewer
The log viewer surfaces `Crosspose.Core` diagnostics and the Doctor CLI output for troubleshooting and support capture.

## Related docs
- [Crosspose.Doctor](../crosspose.doctor/README.md) for CLI behavior.
- [Setup](../setup.md) for installing prerequisites.
