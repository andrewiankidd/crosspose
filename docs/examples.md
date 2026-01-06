# Examples

## Native docker
```powershell
# List all containers (Windows containers)
docker ps -a

# Compose up/down
docker compose up -d
docker compose down
```

## Podman (in WSL) from Windows shell
```powershell
# Using dedicated crosspose-data distro
wsl --distribution crosspose-data --exec podman ps -a
wsl --distribution crosspose-data --exec podman compose up -d
wsl --distribution crosspose-data --exec podman compose down
```

## Crosspose CLI (aggregated view)
```powershell
# Combined docker + podman ps output
dotnet run --project src/Crosspose.Cli -- ps -a

# Run compose across both platforms (provide the folder that contains docker-compose.*.yml files)
dotnet run --project src/Crosspose.Cli -- compose up --dir C:\temp\dekompose-outputs\my-workload

# Target a single workload from a multi-workload output
dotnet run --project src/Crosspose.Cli -- compose up --dir C:\temp\dekompose-outputs --workload core -d

# Use a zipped definition bundle
dotnet run --project src/Crosspose.Cli -- up --dir C:\temp\dekompose-outputs\bundle.zip -d
```

## Dekompose chart-to-compose
```powershell
# Render a chart with values to compose outputs
dotnet run --project src/Crosspose.Dekompose -- --chart C:\path\to\chart --values C:\path\to\values.yaml --output dekompose-outputs

# Merge a dekompose.yml before rendering (overrides or extends crosspose.yml)
dotnet run --project src/Crosspose.Dekompose -- --chart C:\path\to\chart --dekompose-config C:\path\to\dekompose.yml --infra --remap-ports
```
