# Roadmap / Ideas

Unordered feature ideas across the tool's personas: dev workstation tool, deployment orchestrator, portable demo kit.

---

## UX / UI Polish

### Button order and colour consistency
Action buttons across views (Projects, Containers, Images, Volumes) use inconsistent ordering and colour weight. Destructive actions (Delete, Prune) should be visually distinct and consistently placed last. Primary actions (Up, Start) should have stronger visual weight than secondary ones (Inspect, Logs).

### Status at a glance
Container and project rows should show a colour-coded health dot (green/amber/red/grey) that updates on the refresh interval without requiring the user to open details.

### Toolbar context sensitivity
Currently all toolbar buttons render even when disabled. Consider hiding or collapsing buttons that are structurally unavailable for the current selection (e.g. no "Down" button when nothing is running).

### Notification toasts
Background operations (AutoFix, port proxy application, image prune) complete silently. A brief non-blocking toast in the corner ("Port proxy rules applied", "Fix failed: ...") would surface outcomes without requiring the log viewer to be open.

---

## Developer Workflow

### Volume mount editor
GUI control to bind-mount a local folder into a running or to-be-started service — "mount my checkout at /app/src" — without editing compose files by hand. Useful for live reload / hot-swap debugging.

### Env var overrides per service
Inline editor in the Projects or Containers view to set or override environment variables for a service before or after `up`. Values stored as a `.env` override file beside the compose file. Supports masked display for secrets.

### `.env` file import
Drop a `.env` file onto a project to bulk-import variable overrides. Preview diff before applying.

### Debugger attach shortcut
"Attach debugger" button on a running container that opens Visual Studio / VS Code with the right remote debug configuration (container ID, port, source map). Requires knowing the image's debug port — could be declared in `dekompose.custom-rules`.

### Per-service log filtering
Log viewer currently shows a single container stream. A merged multi-service log view with per-service colour coding and keyword filter would replace the need to open multiple details windows.

### Aggregate health / resource dashboard
Single view showing CPU%, memory, and health status for all running services across Docker and Podman, similar to `docker stats` but unified.

---

## Portable Demo Kit

### Windows image portability via `docker save` / `docker load`

**Current gap**: Linux is fully portable — the crosspose-data WSL distro VHD lives at `AppData\crosspose\wsl\crosspose-data\` and travels with the portable folder. Windows (Docker Desktop) images are stored in Docker's own data root (`%LOCALAPPDATA%\Docker\wsl\data\`) and are not covered by the current portable migration.

**Why not `--cache-from type=local`**: That is BuildKit's *build* cache — it caches layer operations during `docker build` and does not apply to pre-built images pulled from a registry. Not relevant here.

**Why not a local registry mirror**: Running a `registry:2` mirror container pre-populated with images and pointing `daemon.json` at `registry-mirrors` would work, but requires a running registry process as part of the portable bundle and a Docker daemon restart to apply. More moving parts than needed.

**No native Docker equivalent**: `--data-root` moves all Docker storage but requires a daemon restart and is managed by Docker Desktop, not an env var. `--registry-mirror` requires a running registry server. `DOCKER_CONFIG` controls auth only. There is no `IMAGE_CACHE_DIR`.

**Design — two Doctor checks + a delete hook**:

The existing Doctor `AutoFix` + `CheckIntervalSeconds` infrastructure handles periodic sync with no new daemon or watcher needed.

*`PortableImageSaveCheck`* — periodically compares `docker images` against `image-manifest.json`. Any image present in Docker but absent from AppData is unsaved. `FixAsync` runs `docker save` for each. `AutoFix = true`.

*`PortableImageLoadCheck`* — periodically compares `.tar` files in `AppData\crosspose\images\windows\` against locally loaded Docker images. Any tar not present in Docker is unloaded. `FixAsync` runs `docker load -i` for each. `AutoFix = true`. This is the fresh-machine bootstrap path — Doctor's AutoFix runs on startup and loads everything before `crosspose up` is ever called.

*Scoping* — both checks should only run in portable mode. Cleanest fit: register them as additional checks (into `doctor.additional-checks`) when portable mode is activated, same pattern as `port-proxy:` and `azure-acr-auth:` checks. They are unregistered if portable mode is disabled.

*Delete hook* — not a Doctor check. When `DockerContainerRunner.RemoveImageAsync` succeeds and portable mode is on, immediately delete the corresponding `.tar` from AppData and remove the entry from `image-manifest.json`. Same applies to `crosspose images rm` in the CLI. This closes the loop: delete in one place, gone everywhere.

*Manifest*: `image-manifest.json` maps `"image:tag" → "filename.tar"` for O(1) lookup. Both checks read and write it as the source of truth.

**Volumes**: Docker volumes live inside Docker's internal VHD and are harder to export. Lower priority for demo kits (apps typically initialise volumes on first start). Can be addressed later with `docker run --rm -v <vol>:/data busybox tar` streaming approach.

**Result**: shipping a portable folder gives a fully offline demo kit — Linux images/state via WSL VHD, Windows images via saved tars, Doctor handles WSL/Podman/Docker setup on the new machine.

---

### One-click demo launch
A "Demo" mode that takes a pre-baked bundle (tgz of compose files + conversion-report + .env defaults) and does the full flow — extract, apply proxies, `up` — from a single button, with a progress wizard.

### Preset profiles
Named profiles stored in `crosspose.yml` that capture a specific chart + values + dekompose-config combination. Switch between "dev", "demo", "staging" profiles without navigating the chart/values flow each time.

### Bundle export with embedded config
Export a project as a self-contained zip that includes the compose files, the resolved env overrides, and a `crosspose-launch.json` manifest so someone else can `crosspose deploy bundle.zip` on a clean machine and have it work.

### Offline bundle registry
A local "shelf" of pre-pulled bundles (like a local OCI cache) so demos can run without internet access. Pull once, run anywhere (on the same or another Windows machine with Crosspose installed).

---

## Config / Secret Management

### `crosspose.yml` GUI editor
Simple key/value form editor for the most common `crosspose.yml` settings (WSL distro, deployment directory, dark mode, offline mode) so users don't need to edit YAML by hand.

### Secret store integration
Allow secrets declared in `dekompose.custom-rules` to be pulled from Windows Credential Manager or a local vault rather than stored in plain YAML. Masked display in UI; resolved at `up` time.

### Multi-values profiles
Support multiple named values files per chart (dev.values.yaml, demo.values.yaml) and let the GUI switch between them without re-running Dekompose from scratch.

---

## Observability / Doctor

### Doctor check history
Persist a timestamped log of check results so you can see when a check last failed, how often it flaps, and what the fix output was. Useful for diagnosing recurring issues.

### Doctor dry-run mode
A `--dry-run` flag (and GUI equivalent) that shows what each `FixAsync` would do without executing it. Useful in corporate environments where elevation requires justification.

### AutoFix opt-out per check
Per-check toggle in Doctor.Gui to disable AutoFix for checks that are noisy or trigger unwanted UAC prompts (e.g. `WslToWindowsFirewallCheck` on machines with PAM tools like Admin By Request).

### Check scheduling visibility
Show the `CheckIntervalSeconds` value for each check in the Doctor.Gui list so users can see how often background re-checks will fire.

---

## Multi-machine / Team

### Bundle sharing
Push/pull bundles to/from a shared UNC path or Azure Blob container. Team members can pull the latest demo bundle without going through the full Dekompose flow.

### Team `crosspose.yml` base
Support a `crosspose.base.yml` loaded from a shared location (network drive, repo) that provides shared chart sources and custom rules, overridable by the local `crosspose.yml`.

### Deployment audit log
Record who ran `up`/`down`/`deploy` and when, to a local append-only log. Useful for shared QA/demo machines where multiple people use the same Crosspose install.

---

## Dekompose Enhancements

### Visual service dependency graph
Render the `depends_on` relationships from generated compose files as a graph in Dekompose.Gui so users can understand startup order before running.

### Incremental re-dekompose
Detect when only a values file has changed (not the chart) and re-run Dekompose for just the affected workload, preserving the rest of the output directory.

### Chart version pinning and update alerts
Track the chart version used per deployment and notify when a newer version is available in the configured source.
