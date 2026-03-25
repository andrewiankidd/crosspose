# CLI Contracts

Argument parsing, exit codes, and output formats for all three CLI executables.

## Common Patterns

All three CLIs share:
- **Top-level statements** in `Program.cs` (no `Main` method, no class wrapper)
- **Manual arg parsing** via `Queue<string>` — no System.CommandLine or other parsing library
- **LaunchedOutsideShell() guard** — checks `PROMPT` and `PSModulePath` env vars; exits with code 1 and a message if both are absent (prevents double-click execution from Explorer)
- **Logging** via `CrossposeLoggerFactory.Create(LogLevel.Information)`

## Crosspose.Doctor

### Arguments
| Flag | Description |
|------|-------------|
| `--fix`, `-f` | Attempt automated fixes for failed checks |
| `--help`, `-h`, `/?` | Show usage text |
| *(no args)* | Run checks in report mode |

### Exit Codes
| Code | Meaning |
|------|---------|
| 0 | All checks passed (or `--help`) |
| 1 | One or more checks failed, or launched outside shell |

### Output Format
Structured log output via `Microsoft.Extensions.Logging.Console`:
```
HH:mm:ss info    crosspose.doctor: Checking: docker-compose
HH:mm:ss info    crosspose.doctor: ✔ docker-compose: Docker Compose version v2.x.x
HH:mm:ss warn    crosspose.doctor: ✖ helm: helm not available.
```

## Crosspose.Dekompose

### Arguments
| Flag | Description |
|------|-------------|
| `--chart <path>` | Helm chart directory to template |
| `--values <file>` | Optional values.yaml for helm |
| `--manifest <file>`, `--rendered-manifest <file>` | Pre-rendered manifest (skips helm) |
| `--output <dir>` | Output directory (default: `./docker-compose-outputs`) |
| `--help`, `-h`, `/?` | Show usage text |

### Argument Validation
- At least one of `--chart` or `--manifest` must be provided.
- `--chart` and `--manifest` are mutually exclusive in practice: if `--manifest` is set, helm rendering is skipped; if `--chart` is set, helm rendering produces the manifest.
- Unknown arguments print a warning and set `ShowHelp = true`.

### Exit Codes
| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Missing manifest, helm failure, launched outside shell, or `--help` |

### Output Files
Written to `--output` directory (default `./docker-compose-outputs/`):
- `rendered.<yyyyMMddHHmmss>.yaml` — raw helm template output (when using `--chart`)
- `rendered.manifest.yaml` — copy of the input manifest
- `TODO.compose-generation.md` — placeholder with porting instructions

## Crosspose.Cli

### Commands

#### `ps [--all|-a]`
Lists containers from both Docker and Podman.

Output format:
```
OS  PLATFORM CONTAINER            IMAGE                    STATUS
win docker   my-container         my-image:latest          Up 2 hours
lin podman   other-container      other-image:latest       Exited (0) 1 hour ago
```
- `OS` column: `win` or `lin`
- Container names and images truncated to 20/24 chars with `…`

#### `compose [--action <val>] [--workload <name>]`
**Stub only.** Logs a message pointing to the PowerShell prototype. Default action: `"status"`.

### Arguments
| Flag | Description |
|------|-------------|
| `--help`, `-h`, `/?` | Show usage text |
| `ps` | List containers |
| `ps -a` / `ps --all` | Include stopped containers |
| `compose --action <val>` | Compose action (stub) |
| `compose --workload <name>` | Target workload (stub) |

### Exit Codes
| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Unknown command, launched outside shell |
