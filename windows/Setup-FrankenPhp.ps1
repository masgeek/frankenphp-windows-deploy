[CmdletBinding()]
param(
    [string] $AppPath = 'C:\app',
    [string] $InstallPath = 'C:\FrankenPHP',
    [ValidateRange(1, 65535)]
    [int] $Port = 8001,
    [ValidateRange(1, 65535)]
    [int] $AdminPort = 2020,
    [ValidateRange(1, 64)]
    [int] $Workers = 2,
    [ValidateRange(1, 10000)]
    [int] $MaxRequests = 250,
    [string] $SqlServerDriverVersion = '5.13.3',
    [string] $RedisExtensionVersion = '6.3.0',
    [string] $ServiceName = 'laravel-frankenphp',
    [string] $ServiceDisplayName = 'Laravel - FrankenPHP',
    [string] $ServiceDescription = 'FrankenPHP and Laravel Octane service.',
    [string] $FirewallRuleName = 'Laravel FrankenPHP',
    [string] $HealthPath = '/up',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [switch] $Development,
    [switch] $OpenFirewall,
    [switch] $ForceFrankenPhpDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = `
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Write-Step([string] $Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from an elevated PowerShell session.'
    }
}

function Invoke-CheckedCommand {
    param(
        [string] $Executable,
        [string[]] $Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $Executable $($Arguments -join ' ')"
    }
}

Assert-Administrator

if (Test-Path $ConfigPath -PathType Leaf) {
    $deploymentConfig = Import-PowerShellDataFile $ConfigPath
    foreach ($parameterName in @(
        'AppPath', 'InstallPath', 'Port', 'AdminPort', 'Workers', 'MaxRequests',
        'SqlServerDriverVersion', 'RedisExtensionVersion', 'ServiceName',
        'ServiceDisplayName', 'ServiceDescription', 'FirewallRuleName', 'HealthPath'
    )) {
        if (-not $PSBoundParameters.ContainsKey($parameterName) -and $deploymentConfig.ContainsKey($parameterName)) {
            Set-Variable -Name $parameterName -Value $deploymentConfig[$parameterName]
        }
    }
}

$installPathFromConfig = $false
if ((Test-Path $ConfigPath -PathType Leaf) -and $deploymentConfig.ContainsKey('InstallPath') -and
    (-not $PSBoundParameters.ContainsKey('InstallPath'))) {
    $installPathFromConfig = $true
}

if ($Port -eq $AdminPort) {
    throw 'The public and admin ports must be different.'
}

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$appPathCache = Join-Path $scriptPath '.cache'
$installPathCache = Join-Path $scriptPath '.install-path.cache'
if (-not $PSBoundParameters.ContainsKey('InstallPath') -and -not $installPathFromConfig -and
    (Test-Path $installPathCache -PathType Leaf)) {
    $cachedInstallPath = [IO.File]::ReadAllText($installPathCache).Trim()
    if (-not [string]::IsNullOrWhiteSpace($cachedInstallPath)) {
        $InstallPath = $cachedInstallPath
    }
}
if (-not $PSBoundParameters.ContainsKey('AppPath')) {
    if (Test-Path $appPathCache -PathType Leaf) {
        $cachedAppPath = [IO.File]::ReadAllText($appPathCache).Trim()
        if (-not [string]::IsNullOrWhiteSpace($cachedAppPath)) {
            $AppPath = $cachedAppPath
        }
    }

    $selectedAppPath = Read-Host "Application directory [$AppPath]"
    if (-not [string]::IsNullOrWhiteSpace($selectedAppPath)) {
        $AppPath = $selectedAppPath
    }
}

$AppPath = [IO.Path]::GetFullPath($AppPath)
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$artisan = Join-Path $AppPath 'artisan'
$envFile = Join-Path $AppPath '.env'
$frankenPhp = Join-Path $InstallPath 'frankenphp.exe'
$php = Join-Path $InstallPath 'php.exe'
$phpIni = Join-Path $InstallPath 'php.ini'
$caddyFile = Join-Path $InstallPath 'Caddyfile'
$legacyServiceExecutable = Join-Path $InstallPath 'frankenphp-service.exe'
$serviceId = $ServiceName

if (-not (Test-Path $artisan -PathType Leaf)) {
    throw "Laravel was not found at $AppPath"
}

if (-not (Test-Path $envFile -PathType Leaf)) {
    throw "The application environment file is missing: $envFile"
}

[IO.File]::WriteAllText($appPathCache, $AppPath, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($installPathCache, $InstallPath, [Text.UTF8Encoding]::new($false))

Write-Step 'Checking Servy'
$servyCommand = Get-Command servy-cli -ErrorAction SilentlyContinue
if (-not $servyCommand) {
    throw 'Servy must be installed separately and servy-cli must be available in PATH.'
}

$servyCli = $servyCommand.Source
Invoke-CheckedCommand $servyCli @('version')

Write-Step 'Stopping the existing FrankenPHP service when present'
$existingService = Get-Service -Name $serviceId -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne 'Stopped') {
    Stop-Service -Name $serviceId -Force
    $existingService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

if ($existingService -and (Test-Path $legacyServiceExecutable -PathType Leaf)) {
    $servicePath = (Get-CimInstance Win32_Service -Filter "Name='$serviceId'").PathName
    if ($servicePath -and $servicePath.IndexOf($legacyServiceExecutable, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        Write-Step 'Removing the legacy WinSW service'
        Invoke-CheckedCommand $legacyServiceExecutable @('uninstall')

        for ($attempt = 1; $attempt -le 15; $attempt++) {
            if (-not (Get-Service -Name $serviceId -ErrorAction SilentlyContinue)) {
                break
            }

            Start-Sleep -Seconds 1
        }

        if (Get-Service -Name $serviceId -ErrorAction SilentlyContinue) {
            throw 'The legacy FrankenPHP service is still pending deletion. Wait briefly and run setup again.'
        }

        $existingService = $null
    }
}

Write-Step 'Installing FrankenPHP'
if ($ForceFrankenPhpDownload -or -not (Test-Path $frankenPhp -PathType Leaf)) {
    $archive = Join-Path $env:TEMP "frankenphp-windows-$PID.zip"

    try {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
        Invoke-WebRequest `
            -UseBasicParsing `
            -Uri 'https://github.com/php/frankenphp/releases/latest/download/frankenphp-windows-x86_64.zip' `
            -OutFile $archive
        Expand-Archive -Path $archive -DestinationPath $InstallPath -Force
    } finally {
        Remove-Item $archive -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $frankenPhp -PathType Leaf) -or -not (Test-Path $php -PathType Leaf)) {
    throw 'The FrankenPHP Windows archive is incomplete.'
}

Invoke-CheckedCommand $frankenPhp @('version')

Write-Step 'Updating PHP and Caddy configuration'
& (Join-Path $scriptPath 'Update-FrankenPhpPhpIni.ps1') -InstallPath $InstallPath
Copy-Item (Join-Path $scriptPath 'Caddyfile') $caddyFile -Force

$env:PHPRC = $phpIni
$env:FRANKENPHP_EXT_DIR = Join-Path $InstallPath 'ext'

Write-Step 'Installing Microsoft SQL Server PHP drivers'
& (Join-Path $scriptPath 'Install-FrankenPhpSqlServerDrivers.ps1') `
    -InstallPath $InstallPath `
    -DriverVersion $SqlServerDriverVersion

Write-Step 'Installing the PHP Redis extension'
& (Join-Path $scriptPath 'Install-FrankenPhpRedisExtension.ps1') `
    -InstallPath $InstallPath `
    -ExtensionVersion $RedisExtensionVersion

Write-Step 'Publishing the FrankenPHP runtime environment'
& (Join-Path $scriptPath 'Set-FrankenPhpSystemPath.ps1') -InstallPath $InstallPath

Write-Step 'Validating the FrankenPHP runtime'
& (Join-Path $scriptPath 'Test-FrankenPhp.ps1') `
    -FrankenPhp $frankenPhp `
    -AppPath $AppPath `
    -PhpIni $phpIni

Write-Step 'Installing and starting the FrankenPHP service'
& (Join-Path $scriptPath 'Install-FrankenPhpService.ps1') `
    -AppPath $AppPath `
    -InstallPath $InstallPath `
    -Port $Port `
    -AdminPort $AdminPort `
    -Workers $Workers `
    -MaxRequests $MaxRequests `
    -ServiceName $ServiceName `
    -ServiceDisplayName $ServiceDisplayName `
    -ServiceDescription $ServiceDescription `
    -FirewallRuleName $FirewallRuleName `
    -HealthPath $HealthPath `
    -Development:$Development `
    -OpenFirewall:$OpenFirewall

Write-Host 'IIS was not modified and remains available for rollback.' -ForegroundColor Green
