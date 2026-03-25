# External Tools

Every external CLI tool invoked by Crosspose, with exact arguments and expected output.

All invocations go through `ProcessRunner.RunAsync`. The caller checks `ProcessResult.IsSuccess` (exit code 0).

## docker

| Caller | Command | Arguments | Output Format |
|--------|---------|-----------|---------------|
| DockerContainerRunner | `docker` | `ps -a --no-trunc --format json` | Newline-delimited JSON objects OR JSON array |
| DockerContainerRunner | `docker` | `ps --no-trunc --format json` | Same, running only |
| DockerContainerRunner | `docker` | `images --no-trunc --format json` | Newline-delimited JSON or array |
| DockerContainerRunner | `docker` | `volume ls --format json` | Newline-delimited JSON or array |
| DockerContainerRunner | `docker` | `start <id>` | Text |
| DockerContainerRunner | `docker` | `stop <id>` | Text |
| DockerComposeCheck | `docker` | `compose version` | Version string on stdout |

### Docker ps JSON fields used
`ID`, `Names`, `Image`, `Status`, `State`, `Ports`, `Labels`

### Docker images JSON fields used
`Repository`, `Tag`, `ID`, `Size`

### Docker volumes JSON fields used
`Name`

### Docker Labels format
Comma-separated string: `"com.docker.compose.project=myproject,com.docker.compose.service=web"`

## docker-compose (legacy)

| Caller | Command | Arguments |
|--------|---------|-----------|
| DockerComposeCheck | `docker-compose` | `--version` |

Fallback check if `docker compose version` fails.

## podman

| Caller | Command | Arguments | Output Format |
|--------|---------|-----------|---------------|
| PodmanContainerRunner | `podman` | `ps -a --format json` | JSON array |
| PodmanContainerRunner | `podman` | `ps --format json` | JSON array |
| PodmanContainerRunner | `podman` | `images --format json` | JSON array |
| PodmanContainerRunner | `podman` | `volume ls --format json` | JSON array |
| PodmanContainerRunner | `podman` | `start <id>` | Text |
| PodmanContainerRunner | `podman` | `stop <id>` | Text |

### Podman ps JSON fields used
`Id` (note: lowercase `d`, unlike Docker's `ID`), `Names`, `Image`, `Status`, `State`, `Ports`, `Labels`

### Podman Labels format
JSON object: `{"com.docker.compose.project": "myproject"}`

### Podman table fallback
If JSON parsing fails, `TableParseFallback` regex-splits lines by 2+ whitespace characters. Expects standard `podman ps` tabular output with headers.

## helm

| Caller | Command | Arguments | Working Directory |
|--------|---------|-----------|-------------------|
| HelmTemplateRunner | `helm` | `template crosspose "<chartPath>" [--values "<valuesPath>"]` | `chartPath` |
| HelmCheck | `helm` | `version --short` | Current directory |

### helm template output
Multi-document YAML written to stdout. Captured and written to `rendered.<timestamp>.yaml`.

## wsl

| Caller | Command | Arguments |
|--------|---------|-----------|
| WslCheck | `wsl` | `--status` |
| WslCheck (fix) | `wsl` | `--install` |
| CrossposeWslCheck | `wsl` | `-l -v` |
| CrossposeWslCheck | `wsl` | `-d crosspose-data -- echo ok` |
| CrossposeWslCheck (fix) | `wsl` | `--install -d Alpine` |
| CrossposeWslCheck (fix) | `wsl` | `--export Alpine "<tempTar>"` |
| CrossposeWslCheck (fix) | `wsl` | `--import crosspose-data "<targetRoot>" "<tempTar>"` |
| CrossposeWslCheck (fix) | `wsl` | `-d crosspose-data -- sh -c "id -u <user> >/dev/null 2>&1 \|\| adduser -D -s /bin/sh <user>"` |
| CrossposeWslCheck (fix) | `wsl` | `-d crosspose-data -- sh -c "echo '<user>:<pass>' \| chpasswd"` |
| PodmanContainerRunner (WSL mode) | `wsl` | `podman ps -a --format json` (etc.) |

### wsl -l -v output format
```
  NAME            STATE           VERSION
* Ubuntu          Running         2
  Alpine          Stopped         2
  crosspose-data  Running         2
```
`DistroExists` does a case-insensitive substring match on each line.

## winget

| Caller | Command | Arguments |
|--------|---------|-----------|
| DockerComposeCheck (fix) | `winget` | `install -e --id Docker.DockerDesktop -h` |
| HelmCheck (fix) | `winget` | `install -e --id Helm.Helm -h` |
| DockerComposeCheck/HelmCheck | `winget` | `--version` (availability check) |

Flags: `-e` = exact match, `-h` = silent/headless install.
