# Setup

These prerequisites are needed for the CLI and GUI experiences:

## Windows features
- **WSL**: Enable Windows Subsystem for Linux (`wsl --install`). Required for Podman support and the crosspose-data Alpine instance.

## Container tooling
- **Docker Desktop**: Provides Windows containers and docker compose. Needed for `docker compose`, `docker ps`, and Windows workloads.
- **Podman** (inside WSL): For Linux containers when running side-by-side. Ensure `podman` is installed in your chosen WSL distro.
- **docker-compose / docker compose**: Comes with Docker Desktop; required by Dekompose outputs.

## Helm and chart tooling
- **Helm 3**: Required to render charts and fetch defaults (`helm repo list`, `helm search repo`, `helm show values`). Crosspose can auto-download helm if missing (HelmClient).

## Optional helpers
- **winget**: Doctor uses it to install Docker Desktop and Helm when running with `--fix`.
- **Windows port proxy**: Windows workloads that call Podman services require a `netsh interface portproxy` entry per exposed infra port. Crosspose.Doctor registers a `port-proxy:<port>@<network>` check automatically; run Doctor with `--fix` (or use the GUI) to create the bridge and matching firewall rule.

## Configuration defaults
Crosspose stores its shared defaults in `%APPDATA%\crosspose\crosspose.yml`. Customize the `compose.wsl` section if you need different credentials for the dedicated `crosspose-data` WSL distro that Doctor provisions.

Add chart-specific translations under `dekompose.custom-rules` so Dekompose knows which local infra containers (SQL, Service Bus, Azurite, etc.) to spin up and which secrets to rewrite. When those infra entries expose host ports, Doctor will persist the matching `port-proxy:<port>@<network>` additional check to keep the Windows/WSL networking path healthy.
