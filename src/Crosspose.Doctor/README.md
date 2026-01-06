# Crosspose.Doctor

Doctor is a lightweight prerequisite checker (similar to `flutter doctor`) for Crosspose. It validates that docker compose, WSL, and Helm are available, and can optionally attempt remediation.

## Current state
- Default checks: docker compose, Docker running, Docker Windows mode, WSL, WSL memory limit, sudo, Crosspose WSL distro, Podman in WSL (plus cgroup/compose checks), Helm.
- Additional checks: `azure-cli`, `azure-acr-auth-win:<registry>`, `azure-acr-auth-lin:<registry>`, and `port-proxy:<port>@<network>` entries enabled via `--enable-additional` (alias `--enable-optional`) or persisted automatically in `crosspose.yml`.
- Shares check/fix implementations with the WPF front-end in `src/Crosspose.Doctor.Gui`.

## Commands & options
- `--fix`, `-f`: Attempt to fix issues (best effort).
- `--enable-additional <key>`: Enable additional checks (`azure-cli`, `azure-acr-auth-win:<registry>`, `azure-acr-auth-lin:<registry>`, `port-proxy:<port>@<network>`). Repeat the flag to enable multiple. `--enable-optional` remains as an alias.
- `--help`: Show help text.
- `--version`, `-v`: Show version.

## Usage examples
```powershell
# Show status only
dotnet run --project src/Crosspose.Doctor --

# Attempt fixes via winget / wsl --install
dotnet run --project src/Crosspose.Doctor -- --fix

# Run with Azure-focused additional checks
dotnet run --project src/Crosspose.Doctor -- --enable-additional azure-cli --enable-additional azure-acr-auth-win:myregistry --enable-additional azure-acr-auth-lin:myregistry
```
