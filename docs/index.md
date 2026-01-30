# Crosspose Documentation

Crosspose converts Helm-rendered Kubernetes manifests into Docker Compose stacks and lets you run Windows and Linux containers side-by-side on a single workstation. The toolchain is split into small CLI and WPF apps that share orchestration code so you can script or click through the same flows.

## Why Crosspose? (reasoning / disclaimer)
Assume you are a developer asked to run a complex multi-service system locally. Typical hurdles:
- Sourcing code and assets (clone repos, find manifests, hunt for scripts).
- Machine setup (SDKs, runtimes, CLIs).
- Build steps (stack switching, target frameworks, platform quirks).
- App configuration (env vars, config rewrites, extra processes).
- Dependency setup (email/SMS, SQL, storage, service bus).

Containers help - reproducible runtimes, identical images across dev/test/prod, infra as code - but orchestration gets tricky:
- Kubernetes: Windows images require Windows Server; local k8s variants typically only run Linux images.
- Docker Desktop: can run Windows or Linux mode, not both side-by-side.
- Result: you often cannot run the full workload (win + lin) on one machine; most guidance says it is impossible.

Workaround: run Windows workloads on Docker Desktop (Windows mode) and Linux workloads on Podman inside WSL. Now you can host both kinds of containers together, but expecting every dev/QA to wire Docker + WSL + Podman and juggle shells is a lot.

Crosspose steps in as a cross-platform compose shim for Docker on Windows and Podman in WSL. It brokers commands and presents a unified view, with CLIs and GUIs built on the same core. See the project overview in the [root README](../README.md).

Example (combined `ps`):
```powershell
# Windows + Linux containers together
dotnet run --project src/Crosspose.Cli -- ps -a
# sample merged output (placeholder)
#   PLATFORM  CONTAINER ID   IMAGE          NAME       STATUS
#   win       1ef9490b...    sample-api     api        Exited (0)
#   win       2723d8ef...    sample-db      db         Exited (255)
#   lin       a1b2c3d4...    nginx:alpine   nginx      Up (healthy)
```

## Bridging Windows containers to WSL services
Many workloads expose Windows front-ends (running on Docker Desktop) that call Linux services hosted by Podman inside WSL. Crosspose.Dekompose rewrites Windows environment variables so `localhost`/`host.docker.internal` and infra host tokens point to the NAT gateway, and Crosspose.Doctor registers a `port-proxy:<port>@<network>` check for every infra port your rules publish. Running Doctor (CLI or GUI) and applying its **Port Proxy** fixes configures:

```
netsh interface portproxy add v4tov4 listenaddress=<nat> listenport=<port> connectaddress=127.0.0.1 connectport=<port>
netsh advfirewall firewall add rule name="port-proxy-<port>" dir=in action=allow protocol=TCP localip=<nat> localport=<port>
```

Crosspose also injects `NAT_GATEWAY_IP` during `crosspose up` so Docker compose can resolve the gateway automatically. This keeps the Windows container -> Podman service bridge declarative without editing compose outputs manually.

### Projects (one line each)
- [Crosspose CLI](crosspose/index.md) - unified ps/compose shim for Docker (Windows) + Podman (WSL). [Repo README](../README.md)
- [Crosspose.Gui](crosspose.gui/index.md) - WPF frontend for containers/images/volumes and tooling.
- [Crosspose.Dekompose](crosspose.dekompose/index.md) - CLI: Helm-to-Compose emitter.
- [Crosspose.Dekompose.Gui](crosspose.dekompose.gui/index.md) - WPF UI to pick chart/repo/values and run Dekompose.
- [Crosspose.Doctor](crosspose.doctor/index.md) - CLI prerequisite checker with additional checks/fixes.
- [Crosspose.Doctor.Gui](crosspose.doctor.gui/index.md) - WPF UI for Doctor with inline fix launches.
- [Crosspose.Core](crosspose.core/index.md) - shared process/helm/platform runners and logging.
- [Configuration](configuration.md) - schema for `crosspose.yml`/`.yaml` and how env vars merge.
- [Portable mode](configuration.md#portable-mode) - run from a self-contained folder with local `AppData`.

## Next steps
- Read the [setup guide](setup.md) for prerequisites.
- Try the [examples](examples.md) to see docker/podman and crosspose side-by-side.
- Review the [configuration reference](configuration.md) to understand `crosspose.yml`/`.yaml`.
- Explore project-specific docs: [Crosspose](crosspose/index.md), [Crosspose.Gui](crosspose.gui/index.md), [Crosspose.Dekompose](crosspose.dekompose/index.md), [Crosspose.Dekompose.Gui](crosspose.dekompose.gui/index.md), [Crosspose.Doctor](crosspose.doctor/index.md), [Crosspose.Doctor.Gui](crosspose.doctor.gui/index.md), [Crosspose.Core](crosspose.core/index.md).
