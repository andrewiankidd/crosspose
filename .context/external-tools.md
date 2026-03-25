# External Tools

Every external CLI invoked by Crosspose, via `ProcessRunner.RunAsync`.

## docker

- `docker ps [-a] --no-trunc --format json` — container list (newline-delimited JSON or array)
- `docker images --no-trunc --format json` — image list
- `docker volume ls --format json` — volume list
- `docker start|stop|rm -f <id>` — container lifecycle
- `docker rmi -f <id>` — image removal
- `docker volume rm -f <name>` — volume removal
- `docker compose version` — version check (Doctor)
- `docker compose -f <file> -p <project> <action> [args]` — compose orchestration
- `docker logs --tail 500 <id>` — container logs (GUI details view)

## podman (via WSL)

- `wsl --distribution <distro> --exec podman ps [-a] --format json` — container list (JSON array)
- Same pattern for `images`, `volume ls`, `start`, `stop`, `rm`, `rmi`, `volume rm`, `logs`
- `wsl --distribution <distro> --exec podman compose -f <file> -p <project> <action>` — compose orchestration

## helm

- `helm template crosspose "<chartPath>" [--values "<values>"] [--version <v>]` — render chart
- `helm version --short` — version check (Doctor)
- `helm repo add|list|update` — repository management
- `helm search repo <name>` — chart search

## wsl

- `wsl --status` — WSL enabled check
- `wsl --install [-d <distro>]` — install WSL/distro
- `wsl -l -v` — list distributions
- `wsl -d <distro> -- echo ok` — distro ping
- `wsl -d <distro> --user <user> -- echo ok` — user auth check
- `wsl --import <name> "<dir>" "<tar>" --version 2` — import distro
- `wsl --unregister <name>` — remove distro
- `wsl -d <distro> -- sh -c "<command>"` — run command inside distro

## winget

- `winget install -e --id Docker.DockerDesktop -h` — install Docker Desktop
- `winget install -e --id Helm.Helm -h` — install Helm
- `winget --version` — availability check

## az (Azure CLI)

- `az account show` — Azure CLI auth check (Doctor)
- `az acr login --name <registry>` — ACR auth (Doctor fix)

## netsh (elevated)

- `netsh interface portproxy add v4tov4 listenaddress=<nat> listenport=<port> connectaddress=127.0.0.1 connectport=<port>` — port proxy for Docker↔WSL bridging
- `netsh advfirewall firewall add rule ...` — firewall rule for port proxy
- `netsh interface portproxy show v4tov4` — list existing proxies (Doctor check)

## oras (OCI)

- `oras repo tags <reference>` — list OCI chart tags
