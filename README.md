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

## Usage

```powershell
.\deploy\windows\Install-FrankenPhp.ps1
.\deploy\windows\Setup-FrankenPhp.ps1
.\deploy\windows\Install-FrankenPhpService.ps1
.\deploy\windows\Install-FrankenPhpService.ps1 -Uninstall
.\deploy\windows\Uninstall-FrankenPhp.ps1
```

`Install-FrankenPhp.ps1` only installs and validates the FrankenPHP runtime and caches its install path. Run `Set-FrankenPhpSystemPath.ps1` from an elevated terminal to publish the runtime to the machine PATH, then run `Setup-FrankenPhp.ps1`. Setup configures the application, installs required PHP extensions including Redis and its companion DLLs, updates runtime configuration, validates Laravel, and configures the Servy service. Open a new terminal after setup before running commands such as `cr:dev`.

`Uninstall-FrankenPhp.ps1` removes the FrankenPHP service, firewall rule, machine environment settings, and FrankenPHP runtime directory. It retains the setup cache and does not modify the Laravel application. Use `-KeepInstallPath` to retain the runtime directory. Verbose logs are enabled by default; pass `-WhatIf` to preview changes.

It does not install application dependencies, build assets, run migrations, modify `.env`, or modify IIS.
