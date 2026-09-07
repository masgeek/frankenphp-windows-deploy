# FrankenPHP Windows Deployment

Reusable PowerShell automation for deploying Laravel Octane with FrankenPHP and Servy on Windows Server.

## Requirements

- Windows PowerShell 5.1 or PowerShell 7
- Servy installed with `servy-cli` in `PATH`
- A deployed Laravel application with `.env`, dependencies, assets, and caches already prepared

## Configuration

Create `frankenphp-deploy.psd1` two directories above `windows/Setup-FrankenPhp.ps1`:

```powershell
@{
    AppPath = 'C:\app'
    InstallPath = 'C:\FrankenPHP'
    ServiceName = 'my-app-frankenphp'
    ServiceDisplayName = 'My App - FrankenPHP'
    ServiceDescription = 'FrankenPHP and Laravel Octane service for My App.'
    FirewallRuleName = 'My App FrankenPHP'
    HealthPath = '/up'
    FrankenPhpVersion = 'latest'
    FrankenPhpSha256 = ''
    LogPath = 'C:\logs\frankenphp-install.log'
}
```

Every value can also be passed directly as a script parameter. Explicit parameters take precedence over the configuration file.

The default FrankenPHP installation directory is `C:\FrankenPHP`, avoiding protected `Program Files` permissions for runtime operations. Setup caches a custom install path in `windows/.install-path.cache` so a later uninstall can find it without passing `-InstallPath` again. `FrankenPhpVersion` can pin a release, and `FrankenPhpSha256` can verify its archive.

All Windows scripts support `--help` to display their available parameters without running any deployment actions.

Only machine-level operations require an elevated PowerShell terminal: publishing FrankenPHP to the machine PATH, installing or removing the service, and complete uninstall. Runtime installation, PHP configuration, extension installation, and testing run without elevation when `InstallPath` is writable. The scripts report clearly when administrator privileges are required.

## Workflow

```powershell
.\deploy\windows\Install-Php.ps1
.\deploy\windows\Update-PhpVersionCatalog.ps1
.\deploy\windows\Setup-FrankenPhp.ps1
.\deploy\windows\Install-FrankenPhpService.ps1
.\deploy\windows\Install-FrankenPhpService.ps1 -Uninstall
.\deploy\windows\Uninstall-Php.ps1
```

`Install-Php.ps1` is the unified runtime installer. Without `-Runtime`, it prompts for Regular PHP or FrankenPHP. Use `-Runtime Php` or `-Runtime FrankenPhp` for automation.

Regular PHP installs into `C:\PHP` by default, caches custom paths in `windows/.php-install-path.cache`, updates the user PATH, and uses the development configuration. The installer prompts before installing SQL Server and Redis; use `-InstallSqlServer Yes|No` and `-InstallRedis Yes|No` for scripted runs. Every runtime derives extensions from `<InstallPath>\ext`. Run `Update-PhpVersionCatalog.ps1` to refresh the cached version list in `windows/.php-versions.cache`; `Install-Php.ps1` uses that cache when prompting for a version.

The FrankenPHP setup workflow publishes FrankenPHP to the machine PATH from an elevated terminal. This is required before service setup so the service and new terminals resolve the FrankenPHP runtime.

`Setup-FrankenPhp.ps1` performs Laravel application setup, copies the Caddyfile, installs PHP extensions, validates the application, and installs the service. It requires administrator privileges because it configures the service and machine-level runtime settings.

The lower-level FrankenPHP service, PATH, validation, INI, and extension scripts are kept under `windows/internal/` and are used by the public workflows.

`windows/php.ini-development` enables visible errors, assertions, timestamp validation, and development-friendly limits. The existing `windows/php.ini` remains the deployment configuration.

When run directly, `Update-FrankenPhpPhpIni.ps1` prompts you to choose the Production or Development configuration. Use `-Environment Development` or `-Environment Production` to skip the prompt. An explicit `-SourcePath` overrides the environment selection.

`Uninstall-FrankenPhp.ps1` removes the FrankenPHP service, firewall rule, machine environment settings, and FrankenPHP runtime directory. It retains the setup cache and does not modify the Laravel application. Use `-KeepInstallPath` to retain the runtime directory. Verbose logs are enabled by default; pass `-WhatIf` to preview changes.

`Uninstall-Php.ps1` is the unified runtime removal command. It prompts for Regular PHP or FrankenPHP, or accepts `-Runtime Php` / `-Runtime FrankenPhp`. FrankenPHP removal requires administrator privileges because it removes services, firewall rules, machine PATH entries, and the runtime directory.

It does not install application dependencies, build assets, run migrations, modify `.env`, or modify IIS.
