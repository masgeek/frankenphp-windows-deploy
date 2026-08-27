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

Use `Manage-FrankenPhp.ps1` for an interactive menu to install the runtime, set up the Laravel application, test the runtime, or uninstall FrankenPHP.

Deployment actions automatically request elevation through UAC when required, so an administrator PowerShell terminal is not needed. The initial installation still requires approving the UAC prompt because it changes machine PATH settings and installs services or firewall rules. The interactive manager itself remains non-elevated until an action is selected.

## Usage

```powershell
.\deploy\windows\Install-FrankenPhp.ps1
.\deploy\windows\Setup-FrankenPhp.ps1
.\deploy\windows\Manage-FrankenPhp.ps1
.\deploy\windows\Install-FrankenPhpService.ps1
.\deploy\windows\Install-FrankenPhpService.ps1 -Uninstall
.\deploy\windows\Uninstall-FrankenPhp.ps1
```

`Install-FrankenPhp.ps1` only installs and validates the FrankenPHP runtime, caches its install path, and publishes it to the system PATH. Run it before `Setup-FrankenPhp.ps1`. The setup configures the application, installs required PHP extensions including Redis and its companion DLLs, updates runtime configuration, validates Laravel, and configures the Servy service. Open a new terminal after setup before running commands such as `cr:dev`.

`Uninstall-FrankenPhp.ps1` removes the FrankenPHP service, firewall rule, machine environment settings, and FrankenPHP runtime directory. It retains the setup cache and does not modify the Laravel application. Use `-KeepInstallPath` to retain the runtime directory. Verbose logs are enabled by default; pass `-WhatIf` to preview changes.

It does not install application dependencies, build assets, run migrations, modify `.env`, or modify IIS.
