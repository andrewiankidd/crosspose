# Crosspose.Dekompose (CLI)

This page captures the full chart-to-compose pipeline, output layout, and customization features that go beyond the project README.

## Prereqs
- `helm` on PATH (v3+).
- For OCI charts in Azure Container Registry: `az` CLI on PATH, logged in (`az login`).

## Output layout
Each run writes a folder (default under `dekompose-outputs`) that contains:

- `docker-compose.<workload>.<os>.yml` per workload/OS.
- `conversion-report.yaml` with infra and port proxy metadata used by Crosspose.
- `_chart.yaml` and `_values.yaml` snapshots when templating a chart.
- `secrets/` and `configmaps/` when secrets or config maps are emitted.

If `--compress` is supplied, the output is also zipped.

## Flags and behaviors
- `--chart <path>`: Helm chart directory or OCI ref (`oci://host/repo[:tag]`).
- `--chart-version <v>`: Optional chart version.
- `--values <file>`: Optional values file for templating.
- `--dekompose-config <file>`: Optional `dekompose.yml` merged into `crosspose.yml` before rendering.
- `--manifest <file>`: Skip Helm; use an already-rendered manifest.
- `--output <dir>`: Base output folder (default: `./dekompose-outputs`).
- `--compress`: Also write a zip of the generated output.
- `--infra`: Apply matched `dekompose.custom-rules` and emit infra compose files/secrets.
- `--remap-ports`: Rewrite in-cluster service URLs (`service.default.svc`) to the assigned host ports.

## Custom rules via `crosspose.yml`
Rules live under `dekompose.custom-rules` and are matched against the chart name (glob semantics). Use them to describe infra dependencies and secret rewrites:

```yaml
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
        - name: azureservicebusemulator
          image: mcr.microsoft.com/azure-messaging/service-bus-emulator:latest
          os: windows
          ports:
            - 5672:5672
      secretKeyRefs:
        keyvault:
          - name: credentials-user-appdb-connection-string
            type: literal
            options:
              value: "Data Source={{INFRA[MSSQL].HOSTNAME}},1433;Initial Catalog=local-dev-sqldb-app;User ID=sa;Password={{INFRA[MSSQL].ENVIRONMENT[SA_PASSWORD]}};Encrypt=False"
      env:
        - name: AppDbConnectionString
          resource: mssql
          parameters:
            database: app
        - name-contains: ServiceBusConnectionString
          resource: azureservicebusemulator
```

### Infra targeting (Windows vs Linux)
Set `os: windows` to emit `docker-compose.<name>.windows.yml`, `os: linux` for Podman-only infra, or omit to keep the legacy `.infra.yml` suffix. Any `ports` entry registers a `port-proxy:<port>@<network>` Doctor check.

### Hostname and NAT gateway tokens
`{{INFRA[NAME].HOSTNAME}}` resolves to:
- The compose service name when infra and workload share the same runtime.
- `${NAT_GATEWAY_IP}` when a Windows workload needs to reach Linux infra.

Crosspose injects `NAT_GATEWAY_IP` during `crosspose up` so Windows containers can reach Podman-hosted services.

### File-based secrets
Secrets declared with `type: file` are written to `secrets/<secret>/..data/<filename>` and copied to `secrets/<secret>/<filename>`. Use `convert_from_base64: false` to keep the raw value.

## Related docs
- [Crosspose.Dekompose.Gui](../crosspose.dekompose.gui/index.md) for the WPF front-end.
- [Crosspose.Core](../crosspose.core/index.md) for shared Helm/OCI helpers and process runner.
