# Crosspose.Doctor

Crosspose.Doctor is the prerequisite checker and fixer for the Crosspose toolchain. This page covers check behavior, how additional checks are persisted, and what the fixes do under the hood.

## Built-in checks
- Docker Desktop available and running.
- Docker is in Windows container mode.
- WSL is enabled and the crosspose-data distro exists.
- WSL memory limit and sudo availability.
- Podman and podman-compose inside WSL (including cgroup v2 support).
- Helm 3.
- Azure CLI (for ACR auth workflows).

## Additional checks
Additional checks live under `doctor.additional-checks` in `crosspose.yml`. These are added automatically by the GUIs and Dekompose when needed:

- `azure-acr-auth-win:<registry>`: Docker Desktop logged in to ACR on Windows.
- `azure-acr-auth-lin:<registry>`: Podman logged in inside WSL.
- `port-proxy:<port>@<network>`: Windows NAT forwards a port to `127.0.0.1:<port>`.

Port proxy checks are generated when infra ports are exposed in Dekompose outputs. The check verifies `netsh interface portproxy` entries and the firewall rule for the NAT gateway.

## Fix behavior
When `--fix` is supplied (or the GUI Fix button is used), Doctor will:

- Use winget to install Docker Desktop or Helm when missing.
- Create the `crosspose-data` WSL distro if not present.
- Install or configure Podman and podman-compose in that distro.
- Add `netsh interface portproxy` rules and matching firewall entries for any `port-proxy` checks.

## Configuration notes
- `doctor.additional-checks` is the source of truth for additional checks.
- The Crosspose GUIs and Dekompose update this list as you add infra or registries.

## Related docs
- [Crosspose.Doctor.Gui](../crosspose.doctor.gui/index.md) for the WPF interface.
- [Setup](../setup.md) for prerequisite installation.
