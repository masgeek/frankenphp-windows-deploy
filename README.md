# PHP Manager

A Windows desktop application for managing PHP versions, extensions, and configuration. Built with WPF and .NET 10, self-contained single executable with no dependencies.

## Download

Grab the latest `php-manager-*-win-x64.zip` from [Releases](../../releases). Extract and run `php-manager.exe`.

## Installation Path

By default, PHP versions are installed to `C:\PHP` with each version in its own subdirectory (e.g. `C:\PHP\8.4.3`). FrankenPHP is installed to `C:\PHP\frankenphp`.

To change these paths, go to **Settings** in the sidebar and update:

- **PHP Installation Path** — where PHP versions are stored (default: `C:\PHP`)
- **FrankenPHP Path** — where FrankenPHP is installed (default: `<PHP path>\frankenphp`)

Settings are saved to `settings.json` next to the executable.

## Features

### Version Manager
- Install PHP versions from the official Windows catalog (3 fallback mirrors)
- Switch active version with one click
- Remove installed versions
- Side-by-side management under `C:\PHP\<version>`
- System PATH management with admin elevation

### Extension Manager
- View all extensions from php.ini with enabled/disabled status
- Toggle extensions inline and save
- **Verify Loaded** — runs `php -m` and reports loaded, failed, and missing DLLs

### Extension Installers
- **Redis** — downloads from windows.php.net, auto-detects PHP build (version, thread safety, architecture, compiler)
- **SQL Server** — downloads pdo_sqlsrv + sqlsrv from Microsoft releases

### FrankenPHP
- Install/update FrankenPHP runtime from GitHub releases
- Generate Caddyfile for Laravel Octane (configurable port, admin port, workers)
- Install/remove Windows service via Servy
- Add FrankenPHP to system PATH with admin elevation

### Configuration
- Switch between php.ini-development and php.ini-production
- **CA Certificate** — downloads cacert.pem from curl.se, patches `curl.cainfo` and `openssl.cafile`

## Building from Source

Requires .NET 10 SDK.

```bash
dotnet publish PhpManager/PhpManager.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Output: `publish/php-manager.exe`

## Creating a Release

Releases are automatic. When CI succeeds on a push to `main` and `PhpManager/` changed since the last tag, the **BumpVersion** workflow tags the next version (via `masgeek/github-tag-action`, using the munywele-bot GitHub App token). The release it creates triggers the **Publish Release** workflow, which builds `php-manager-<version>-win-x64.zip`, attests its provenance and attaches it to the release.

BumpVersion needs the repo variable `APP_CLIENT_ID` and secret `APP_PRIVATE_KEY` for the munywele-bot GitHub App (installed on this repo with Contents: read/write). The App token, not `GITHUB_TOKEN`, is what lets the new release trigger Publish Release.

To release a specific version manually, run the **Publish Release** workflow from the Actions tab and enter a version (e.g. `1.2.3`).

## Requirements

- Windows 10/11 x64
- No .NET runtime required (self-contained)
- Admin privileges only for system PATH updates
- Servy required for FrankenPHP service management
