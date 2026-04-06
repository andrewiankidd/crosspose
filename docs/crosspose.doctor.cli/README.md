# Crosspose.Doctor.Cli

Thin CLI entry point for the prerequisite checker. Runs all checks and optionally attempts automated fixes.

## Usage

```powershell
# Check prerequisites
dotnet run --project src/Crosspose.Doctor.Cli

# Attempt automated fixes for failed checks
dotnet run --project src/Crosspose.Doctor.Cli -- --fix

# Include additional checks (ACR auth, port proxy) from crosspose.yml
dotnet run --project src/Crosspose.Doctor.Cli -- --enable-additional
```

## Flags

| Flag | Description |
|------|-------------|
| `--fix` | Run `FixAsync` on each failed check that supports it |
| `--enable-additional` | Load `doctor.additional-checks` from `crosspose.yml` |

## Exit codes
- `0` — all checks passed.
- Non-zero — one or more checks failed. Use `--fix` to attempt remediation.

## Related docs
- [Crosspose.Doctor](../crosspose.doctor/README.md) for check catalogue and fix behavior.
- [Crosspose.Doctor.Gui](../crosspose.doctor.gui/README.md) for the interactive WPF interface.
