# FrankenPHP Windows Deployment

Reusable PowerShell automation for deploying Laravel Octane with FrankenPHP and Servy on Windows Server.

## Requirements

- Elevated Windows PowerShell
- Servy installed with `servy-cli` in `PATH`
- A deployed Laravel application with `.env`, dependencies, assets, and caches already prepared

## Configuration

Create `frankenphp-deploy.psd1` two directories above `windows/Setup-FrankenPhp.ps1`:

```powershell
@{
    AppPath = 'C:\app'
    ServiceName = 'my-app-frankenphp'
    ServiceDisplayName = 'My App - FrankenPHP'
    ServiceDescription = 'FrankenPHP and Laravel Octane service for My App.'
    FirewallRuleName = 'My App FrankenPHP'
    HealthPath = '/up'
}
```

Every value can also be passed directly as a script parameter. Explicit parameters take precedence over the configuration file.

## Usage

```powershell
.\deploy\windows\Setup-FrankenPhp.ps1
.\deploy\windows\Install-FrankenPhpService.ps1
.\deploy\windows\Install-FrankenPhpService.ps1 -Uninstall
.\deploy\windows\Uninstall-FrankenPhp.ps1
```

The setup installs FrankenPHP and required PHP extensions, including Redis and its companion DLLs, updates runtime configuration, validates Laravel, and configures the Servy service. It also puts the selected FrankenPHP directory first in the machine `PATH` and sets `FRANKENPHP_EXT_DIR` for new processes. Open a new terminal after setup before running commands such as `cr:dev`.

`Uninstall-FrankenPhp.ps1` removes the FrankenPHP service, firewall rule, machine environment settings, and FrankenPHP runtime directory. It retains the setup cache and does not modify the Laravel application. Use `-KeepInstallPath` to retain the runtime directory. Verbose logs are enabled by default; pass `-WhatIf` to preview changes.

It does not install application dependencies, build assets, run migrations, modify `.env`, or modify IIS.
