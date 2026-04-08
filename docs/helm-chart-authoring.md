# Helm Chart Authoring for Crosspose

Chart authors can bundle a `crosspose/` directory inside their Helm chart to ship local-dev defaults alongside the chart itself. Crosspose reads these files automatically when the chart is pulled, so users get a working local environment without any manual configuration.

## Directory layout

```
my-chart/
├── Chart.yaml
├── values.yaml
├── templates/
│   └── ...
└── crosspose/
    ├── values.yaml     # local-dev values (overrides chart defaults)
    └── dekompose.yml   # Dekompose rules for this chart
```

Both files are optional. Include neither, one, or both depending on what the chart needs.

## `crosspose/values.yaml`

A standard Helm values file. It should contain values suitable for local development — smaller resource requests, dev image tags, feature flags, and any settings that differ from the production defaults in the chart's root `values.yaml`.

```yaml
replicaCount: 1

image:
  tag: latest

resources:
  requests:
    cpu: 100m
    memory: 128Mi
```

## `crosspose/dekompose.yml`

Dekompose rules specific to this chart. The top-level key is `dekompose:`, and `match:` must equal the chart name as declared in `Chart.yaml`.

```yaml
dekompose:
  custom-rules:
  - match: my-chart          # must match Chart.yaml `name`
    infra:
    - name: mssql
      image: ''
      build:
        dockerfile_inline: |
          FROM mcr.microsoft.com/mssql/server:2022-latest
          USER root
          RUN apt-get update && apt-get install -y mssql-server-fts
          CMD /opt/mssql/bin/sqlservr
      environment:
        ACCEPT_EULA: Y
        SA_PASSWORD: P@ssw0rd!123
        MSSQL_PID: Developer
      ports:
      - 1433:1433
      healthcheck:
        test:
        - CMD-SHELL
        - /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -Q "SELECT 1" -No || exit 1
        interval: 10s
        timeout: 5s
        retries: 5
        start_period: 30s
    secret-key-refs:
      keyvault:
      - name: credentials-db-connection-string
        type: literal
        options:
          value: Data Source={{INFRA[MSSQL].HOSTNAME}};Initial Catalog=local-dev-db;User ID=sa;Password={{INFRA[MSSQL].ENVIRONMENT[SA_PASSWORD]}};Encrypt=False
```

See [configuration.md](configuration.md) for the full `dekompose.custom-rules` schema including `infra`, `secret-key-refs`, and placeholder syntax.

## How Crosspose uses these files

**Via the GUI (automatic):** When a chart is pulled through the Crosspose GUI, the `crosspose/` directory is extracted automatically and the files are written as named siblings next to the chart:

```
%APPDATA%\crosspose\helm-charts\
├── my-chart-1.2.0.tgz
├── my-chart-1.2.0.values.yaml     ← from crosspose/values.yaml
└── my-chart-1.2.0.dekompose.yml   ← from crosspose/dekompose.yml
```

The GUI then picks these up automatically when you select the chart for dekomposition. Existing sibling files are never overwritten, so local customisations are preserved across re-pulls.

**Via the CLI (manual extract):**

```powershell
helm pull oci://your-registry/my-chart --destination .
tar -xzf my-chart-1.2.0.tgz my-chart/crosspose
```

Then pass the extracted paths explicitly:

```powershell
dotnet run --project src/Crosspose.Dekompose.Cli -- `
  --chart my-chart-1.2.0.tgz `
  --values my-chart/crosspose/values.yaml `
  --dekompose-config my-chart/crosspose/dekompose.yml `
  --infra --remap-ports --compress
```

**Supplying external files:** The bundled files are defaults. You can always supply your own with `--values` and `--dekompose-config` regardless of whether the chart embeds a `crosspose/` directory. External files take precedence.

## Reference chart

The [Cross-Platform Helm Chart Hello World](https://github.com/andrewiankidd/CrossPlatformHelmChartHelloWorld) is a minimal chart that implements this standard. It bundles `crosspose/values.yaml` and `crosspose/dekompose.yml` and is used as the Crosspose integration test target.
