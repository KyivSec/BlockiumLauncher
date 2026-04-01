# BlockiumLauncher

BlockiumLauncher is a Minecraft launcher written in C# with a layered backend, a CLI host, and an experimental GTK host. The current priority is the backend and CLI: instance management, launch planning, launch execution, content indexing, and catalog listing for Modrinth and CurseForge.

## Current Status

- CLI is the primary supported host.
- GTK host exists but is still early-stage and not the main development target yet.
- Microsoft account sign-in is not enabled by default. A real implementation requires Microsoft identity app registration and configuration.
- CurseForge catalog access requires an API key.

## Features

- Install, verify, repair, and launch Minecraft instances
- Support for `vanilla`, `fabric`, `quilt`, `forge`, and `neoforge`
- Offline account management
- Launch status tracking and process control
- Shared metadata/runtime/cache layout under one launcher root
- Per-instance content indexing for:
  - mods
  - resourcepacks
  - shaders
  - worlds
  - screenshots
  - servers
- Mod enable/disable support via `.jar` and `.jar.disabled`
- Catalog listing commands for:
  - Modrinth mods, modpacks, resourcepacks, shaders
  - CurseForge mods, modpacks, resourcepacks, shaders
- Timestamped text logging with daily context logs and `latest.log`

## Repository Layout

- `src/BlockiumLauncher.Domain`: core entities and value objects
- `src/BlockiumLauncher.Application`: use cases and application abstractions
- `src/BlockiumLauncher.Infrastructure`: persistence, metadata clients, downloads, launch, logging
- `src/BlockiumLauncher.Contracts`: DTOs for host-facing contracts
- `src/BlockiumLauncher.Host.Cli`: primary executable host
- `src/BlockiumLauncher.Host.GtkSharp`: experimental desktop host
- `tests/*`: unit and integration-style tests by layer

## Prerequisites

- .NET 10 SDK
- Internet access for metadata/catalog queries and game asset/runtime downloads
- Java is required to actually launch Minecraft unless a managed runtime is downloaded/resolved for the target instance

Optional:

- CurseForge API key for CurseForge catalog commands
- GTK 3 runtime/development packages if you want to build or run the GTK host

## Quick Start

Restore and build:

```powershell
dotnet restore BlockiumLauncher.slnx
dotnet build BlockiumLauncher.slnx -c Release
```

Run tests:

```powershell
dotnet test BlockiumLauncher.slnx --no-restore
```

Show CLI help:

```powershell
dotnet run --project src/BlockiumLauncher.Host.Cli -- --help
```

Install an instance:

```powershell
dotnet run --project src/BlockiumLauncher.Host.Cli -- instances install --name TestPack --version 1.21.1 --loader neoforge
```

List Modrinth mods for NeoForge 1.21.1:

```powershell
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog mods --provider modrinth --loader neoforge --game-version 1.21.1
```

## CLI Commands

Current command surface includes:

- `accounts list`
- `accounts add-offline --username <name>`
- `accounts set-default --account-id <id>`
- `accounts remove --account-id <id>`
- `instances install --name <name> --version <version> --loader <vanilla|fabric|quilt|forge|neoforge>`
- `instances verify --instance-id <id>`
- `instances repair --instance-id <id>`
- `instances start --instance-id <id>`
- `launch plan`
- `launch run`
- `launch status`
- `launch stop`
- `catalog mods`
- `catalog modpacks`
- `catalog resourcepacks`
- `catalog shaders`
- `versions vanilla`
- `versions loaders`
- `diagnostics dump`
- `instance content list`
- `instance content rescan`
- `instance mods disable`
- `instance mods enable`

For machine-readable output, most commands also support `--json`.

## Catalog Examples

Modrinth:

```powershell
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog mods --provider modrinth --loader neoforge --game-version 1.21.1 --query sodium
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog modpacks --provider modrinth --game-version 1.21.1 --query skyblock
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog resourcepacks --provider modrinth --game-version 1.21.1
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog shaders --provider modrinth --game-version 1.21.1
```

CurseForge:

```powershell
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog mods --provider curseforge --loader neoforge --game-version 1.21.1
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog modpacks --provider curseforge --game-version 1.21.1
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog resourcepacks --provider curseforge --game-version 1.21.1
dotnet run --project src/BlockiumLauncher.Host.Cli -- catalog shaders --provider curseforge --game-version 1.21.1
```

Common filters:

- `--query <text>`
- `--category <value>` repeated
- `--sort relevance|downloads|follows|newest|updated`
- `--limit <1-100>`
- `--offset <0+>`
- `--json`

## Environment Variables

### CurseForge

CurseForge catalog listing requires an API key. The launcher now supports storing that key in the platform secret store when available:

- Windows: DPAPI-backed encrypted secret file under the launcher data directory
- macOS: Keychain via the `security` command
- Linux: Secret Service via `secret-tool`

Preferred setup:

```powershell
dotnet run --project src/BlockiumLauncher.Cli -- catalog key set
```

You can inspect the current key source without printing the key itself:

```powershell
dotnet run --project src/BlockiumLauncher.Cli -- catalog key status
```

Environment variable fallback:

Windows PowerShell:

```powershell
$env:CURSEFORGE_API_KEY="your-api-key"
```

Linux/macOS shells:

```bash
export CURSEFORGE_API_KEY="your-api-key"
```

### Launcher Data Root

The default launcher root is platform-specific:

- Windows: `%USERPROFILE%\BlockiumLauncher`
- macOS: `~/Library/Application Support/BlockiumLauncher`
- Linux: `$XDG_DATA_HOME/BlockiumLauncher` or `~/.local/share/BlockiumLauncher`

Under that root, the launcher manages directories such as:

- `data`
- `cache`
- `instances`
- `shared`
- `logs`
- `diagnostics`
- `runtimes`

## Logging

Logs are written as plain text files with timestamps.

- Daily context logs use the format `{context}_{yyyyMMdd}.log`
- `latest.log` mirrors the newest entries
- Logs live under the launcher `logs` directory

## Building On Windows

### CLI Host

Build:

```powershell
dotnet restore BlockiumLauncher.slnx
dotnet build BlockiumLauncher.slnx -c Release
```

Run:

```powershell
dotnet run --project src/BlockiumLauncher.Host.Cli --
```

Publish a Windows CLI binary:

```powershell
dotnet publish src/BlockiumLauncher.Host.Cli/BlockiumLauncher.Host.Cli.csproj -c Release -r win-x64 --self-contained false -o artifacts/cli/win-x64
```

### GTK Host

The GTK host is experimental. If you want to build it on Windows, you need a working GTK 3 runtime compatible with GtkSharp in addition to the .NET SDK.

Build:

```powershell
dotnet build src/BlockiumLauncher.Host.GtkSharp/BlockiumLauncher.Host.GtkSharp.csproj -c Release
```

Run:

```powershell
dotnet run --project src/BlockiumLauncher.Host.GtkSharp --
```

## Building On Linux

### CLI Host

Install the .NET 10 SDK using your distribution package source or Microsoft packages, then run:

```bash
dotnet restore BlockiumLauncher.slnx
dotnet build BlockiumLauncher.slnx -c Release
dotnet test BlockiumLauncher.slnx --no-restore
dotnet run --project src/BlockiumLauncher.Host.Cli --
```

Publish a Linux CLI binary:

```bash
dotnet publish src/BlockiumLauncher.Host.Cli/BlockiumLauncher.Host.Cli.csproj -c Release -r linux-x64 --self-contained false -o artifacts/cli/linux-x64
```

### GTK Host

The GTK host is optional and early-stage. Install GTK 3 development/runtime packages first. On Debian/Ubuntu-like systems that is typically:

```bash
sudo apt install libgtk-3-0 libgtk-3-dev
```

Then build/run:

```bash
dotnet build src/BlockiumLauncher.Host.GtkSharp/BlockiumLauncher.Host.GtkSharp.csproj -c Release
dotnet run --project src/BlockiumLauncher.Host.GtkSharp --
```

Package names vary by distribution.

## Building On macOS

### CLI Host

Install the .NET 10 SDK, then run:

```bash
dotnet restore BlockiumLauncher.slnx
dotnet build BlockiumLauncher.slnx -c Release
dotnet test BlockiumLauncher.slnx --no-restore
dotnet run --project src/BlockiumLauncher.Host.Cli --
```

Publish a macOS CLI binary for Apple Silicon:

```bash
dotnet publish src/BlockiumLauncher.Host.Cli/BlockiumLauncher.Host.Cli.csproj -c Release -r osx-arm64 --self-contained false -o artifacts/cli/osx-arm64
```

For Intel Macs, replace `osx-arm64` with `osx-x64`.

### GTK Host

The GTK host is experimental. Install GTK 3 first, for example with Homebrew:

```bash
brew install gtk+3
```

Then build/run:

```bash
dotnet build src/BlockiumLauncher.Host.GtkSharp/BlockiumLauncher.Host.GtkSharp.csproj -c Release
dotnet run --project src/BlockiumLauncher.Host.GtkSharp --
```

Depending on your local setup, you may also need to ensure the native GTK libraries are visible to the runtime.

## Testing

Run the full test suite:

```powershell
dotnet test BlockiumLauncher.slnx --no-restore
```

The repository currently has tests across:

- domain value objects and entities
- application use cases
- infrastructure persistence/download/metadata/launch services
- shared result/error primitives

## Notes And Limitations

- Microsoft authentication is intentionally not configured in the default setup.
- CurseForge support currently covers catalog listing and requires an API key.
- The GTK host is not yet the main supported frontend.
- The launcher is under active development; command surface and persistence details may still evolve.

## License

See [LICENSE.txt](/C:/Users/Admin/source/repos/KyivSec/BlockiumLauncher/LICENSE.txt).
