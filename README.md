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

For Regular PHP, `Install-Php.ps1` delegates to `php-install.ps1`, which manages multiple versions side-by-side under `C:\PHP\<version>`. Extensions and CA certificates are installed per-version. Use `-Version` to install a specific version, or let the script prompt you from the cached catalog.

The FrankenPHP setup workflow publishes FrankenPHP to the machine PATH from an elevated terminal. This is required before service setup so the service and new terminals resolve the FrankenPHP runtime.

`Setup-FrankenPhp.ps1` performs Laravel application setup, copies the Caddyfile, installs PHP extensions, validates the application, and installs the service. It requires administrator privileges because it configures the service and machine-level runtime settings.

The lower-level FrankenPHP service, PATH, validation, INI, and extension scripts are kept under `windows/internal/` and are used by the public workflows.

`windows/php.ini-development` enables visible errors, assertions, timestamp validation, and development-friendly limits. The existing `windows/php.ini` remains the deployment configuration.

When run directly, `Update-FrankenPhpPhpIni.ps1` prompts you to choose the Production or Development configuration. Use `-Environment Development` or `-Environment Production` to skip the prompt. An explicit `-SourcePath` overrides the environment selection.

`Uninstall-FrankenPhp.ps1` removes the FrankenPHP service, firewall rule, machine environment settings, and FrankenPHP runtime directory. It retains the setup cache and does not modify the Laravel application. Use `-KeepInstallPath` to retain the runtime directory. Verbose logs are enabled by default; pass `-WhatIf` to preview changes.

`Uninstall-Php.ps1` is the unified runtime removal command. It prompts for Regular PHP or FrankenPHP, or accepts `-Runtime Php` / `-Runtime FrankenPhp`. FrankenPHP removal requires administrator privileges because it removes services, firewall rules, machine PATH entries, and the runtime directory.

### CA certificate bundle

If you installed PHP without the CA certificate bundle, you can add it later:

```powershell
.\deploy\windows\Set-PhpCacert.ps1
```

The script prompts for the runtime and installation path, downloads `cacert.pem`, and patches `php.ini` with `openssl.cafile`.

### PHP Version Manager

Manage multiple PHP versions side-by-side. Each version is stored in its own subdirectory under `C:\PHP` (e.g., `C:\PHP\8.4.12`, `C:\PHP\8.3.25`).

```powershell
# Install a new PHP version
.\deploy\windows\php-install.ps1

# List installed and available versions
.\deploy\windows\php-list.ps1
.\deploy\windows\php-list.ps1 -Available

# Switch active PHP version
.\deploy\windows\php-use.ps1 -Version 8.4.12

# Show current active version
.\deploy\windows\php-current.ps1

# Remove an installed version
.\deploy\windows\php-remove.ps1 -Version 8.3.25
```

The first version installed is automatically set as active. Switching versions updates the user PATH and `php.ini` location. Extensions and CA certificates are installed per-version.

It does not install application dependencies, build assets, run migrations, modify `.env`, or modify IIS.
