# Setup

## Prerequisites

The only thing you need to install manually is the [.NET SDK](https://dotnet.microsoft.com/download) (version 10 or later).

Everything else — WSL, Helm, Docker Desktop, Podman, port proxy configuration, and firewall rules — is handled automatically by **Crosspose Doctor**. Run it after cloning:

```powershell
dotnet run --project src/Crosspose.Doctor.Cli -- --fix
```

Doctor checks all prerequisites, reports what is missing, and applies fixes where it can. Re-run it any time your environment drifts (e.g. after a Windows update or Docker Desktop upgrade).

## Configuration defaults

Crosspose stores its shared defaults in `%APPDATA%\crosspose\crosspose.yml`. Customise the `compose.wsl` section if you need different credentials for the dedicated `crosspose-data` WSL distro that Doctor provisions.

If a `.portable` file exists beside the executable, Crosspose switches to portable mode and uses `.\AppData\crosspose` next to the EXE instead of `%APPDATA%`. Existing data is moved into the portable folder on first launch (when the target does not already exist).

Add chart-specific translations under `dekompose.custom-rules` so Dekompose knows which local infra containers (SQL, Service Bus, Azurite, etc.) to spin up and which secrets to rewrite. When those infra entries expose host ports, Doctor will persist the matching `port-proxy:<port>@<network>` additional check to keep the Windows/WSL networking path healthy.
