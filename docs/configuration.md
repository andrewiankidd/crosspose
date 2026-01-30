# Crosspose Configuration

Crosspose keeps its shared configuration in `%APPDATA%\crosspose\crosspose.yml` (or `crosspose.yaml` if the `.yml` file does not yet exist). All components read from this single source via `Crosspose.Core.Configuration.CrossposeConfigurationStore`.

## Portable mode

If a `.portable` file exists beside the executable, Crosspose switches to portable mode and stores all app data under `.\AppData\crosspose` next to the EXE. On first launch in portable mode, existing data from `%APPDATA%\crosspose` and `%LOCALAPPDATA%\Crosspose\helm` is moved into the portable folder (when the target does not already exist).

## Schema overview

- `compose.output-directory` - Base folder for `dekompose` outputs and GUI definitions. Defaults to `dekompose-outputs`.
- `compose.deployment-directory` - Root folder where GUI/CLI deployments extract compose projects before orchestrating them. Defaults to `crosspose-deployments`.
- `compose.log-file` - Override where the shared logger writes its file sink (`Crosspose.Core.Logging.CrossposeLoggerFactory`).
- `compose.gui.refresh-interval-seconds` - Refresh cadence for the main GUI views (defaults to 5 seconds).
- `compose.wsl.*` - Default distro/user/password that `Crosspose.Doctor` uses for the dedicated WSL instance when no environment overrides exist.
- `oci-registries` - Registry list maintained by `Crosspose.Core.Orchestration.OciRegistryStore`.
- `doctor.additional-checks` - Doctor-specific additional check keys (`azure-cli`, `azure-acr-auth-win:<registry>`, `azure-acr-auth-lin:<registry>`, `port-proxy:<port>@<network>`, etc.) that should always be enabled.
- `dekompose.custom-rules` - Optional chart-specific translation rules that describe the infrastructure Dekompose should provision plus the secret material to map into compose files.

## Sample `crosspose.yml`

```yaml
# Shared compose configuration
compose:
  # Folder where dekompose writes per-chart outputs (mirrored by the GUI)
  output-directory: dekompose-outputs
  # Where Crosspose extracts runtime deployments
  deployment-directory: crosspose-deployments

  # Optional log path for the shared logger file sink
  log-file: C:\Users\You\AppData\Roaming\crosspose\logs\crosspose.log

  gui:
    # Seconds between auto-refresh ticks in Crosspose.Gui
    refresh-interval-seconds: 5

  wsl:
    # Defaults for doctor/WSL tooling
    distro: crosspose-data
    user: crossposeuser
    password: crossposepassword

# Optional Helm/OCI configuration and doctor flags (managed by the respective tools)
oci-registries: []
doctor:
  additional-checks:
    - azure-cli
    - port-proxy:1433@dekompose-1234567890

# Dekompose translation profile
dekompose:
  custom-rules:
    - match: "*sample-app*"
      infra:
        - name: mssql
          image: mcr.microsoft.com/mssql/server:2022-latest
          ports:
            - 1433:1433
          environment:
            ACCEPT_EULA: Y
            SA_PASSWORD: P@ssw0rd!123
            MSSQL_PID: Developer
      secretKeyRefs:
        keyvault:
          - name: credentials-user-appdb-connection-string
            type: literal
            options:
              value: "Data Source={{INFRA[MSSQL].HOSTNAME}},1433;Initial Catalog=local-dev-sqldb-app;User ID=sa;Password={{INFRA[MSSQL].ENVIRONMENT[SA_PASSWORD]}};Encrypt=False"
```

### `dekompose.custom-rules`

Each rule is matched against the chart name (glob semantics). The rule can declare:

- `infra`: Containers Dekompose should scaffold in addition to the chart workloads. Infra definitions support `image`, `environment`, `command`, `volumes`, `os`, and `ports` just like compose. Host ports automatically trigger a `port-proxy:<port>@<network>` Doctor check so the Windows host forwards traffic from Docker to Podman.
- `secretKeyRefs`: Hierarchical namespaces (for example `keyvault`, `split-io`) that contain literal or file secrets. Secrets support `convert_from_base64` for binary blobs, and placeholders like `{{INFRA[MSSQL].ENVIRONMENT[SA_PASSWORD]}}` resolve to the infra values declared above. Use `{{INFRA[MSSQL].HOSTNAME}}` to point workloads at the right infra endpoint regardless of whether the workload runs in Docker (Windows) or Podman (WSL). If a value begins with a token (for example `value: "{{INFRA[MSSQL].HOSTNAME}},1433"`), wrap it in quotes; YAML plain scalars cannot start with `{`.
- `env`: (See the [Crosspose.Dekompose doc](crosspose.dekompose/index.md)) list of environment rewrite rules that target the infra definitions.

### `doctor.additional-checks`

Doctor automatically honors the list saved in `crosspose.yml`:

- `azure-cli` - always on.
- `azure-acr-auth-win:<registry>` / `azure-acr-auth-lin:<registry>` - tracked per registry automatically when you add them via the GUIs.
- `port-proxy:<port>@<network>` - created by Dekompose whenever an infra definition exposes a Windows host port. The Doctor fix wires up `netsh interface portproxy` plus a firewall rule so Windows containers can reach Podman services.

You can author these keys manually or rely on Crosspose.Dekompose / Crosspose.Dekompose.Gui, which call `DoctorCheckRegistrar.EnsureChecks(...)` after each run.
