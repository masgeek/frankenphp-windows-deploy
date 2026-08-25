[CmdletBinding()]
param(
    [string] $AppPath = 'C:\fee-processor',
    [string] $InstallPath = 'C:\Program Files\FrankenPHP',
    [ValidateRange(1, 65535)]
    [int] $Port = 8080,
    [ValidateRange(1, 65535)]
    [int] $AdminPort = 2019,
    [ValidateRange(1, 64)]
    [int] $Workers = 2,
    [ValidateRange(1, 10000)]
    [int] $MaxRequests = 250,
    [string] $SqlServerDriverVersion = '5.13.3',
    [switch] $BuildAssets,
    [switch] $OpenFirewall,
    [switch] $ForceFrankenPhpDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

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

function Set-DotEnvValue {
    param(
        [string] $Path,
        [string] $Name,
        [string] $Value
    )

    $content = [IO.File]::ReadAllText($Path)
    $pattern = "(?m)^$([Regex]::Escape($Name))=.*$"
    $line = "$Name=$Value"

    if ([Regex]::IsMatch($content, $pattern)) {
        $content = [Regex]::Replace($content, $pattern, $line)
    } else {
        $content = $content.TrimEnd() + [Environment]::NewLine + $line + [Environment]::NewLine
    }

    [IO.File]::WriteAllText($Path, $content, [Text.UTF8Encoding]::new($false))
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

if ($Port -eq $AdminPort) {
    throw 'The public and admin ports must be different.'
}

$AppPath = [IO.Path]::GetFullPath($AppPath)
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$artisan = Join-Path $AppPath 'artisan'
$envFile = Join-Path $AppPath '.env'
$frankenPhp = Join-Path $InstallPath 'frankenphp.exe'
$php = Join-Path $InstallPath 'php.exe'
$phpIni = Join-Path $InstallPath 'php.ini'
$serviceExecutable = Join-Path $InstallPath 'frankenphp-service.exe'
$serviceConfig = Join-Path $InstallPath 'frankenphp-service.xml'
$caddyFile = Join-Path $InstallPath 'Caddyfile'
$serviceId = 'fee-processor-frankenphp'

if (-not (Test-Path $artisan -PathType Leaf)) {
    throw "Laravel was not found at $AppPath"
}

if (-not (Test-Path $envFile -PathType Leaf)) {
    throw "The production environment file is missing: $envFile"
}

Write-Step 'Stopping the existing FrankenPHP service when present'
$existingService = Get-Service -Name $serviceId -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne 'Stopped') {
    Stop-Service -Name $serviceId -Force
    $existingService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

Write-Step 'Installing FrankenPHP'
if ($ForceFrankenPhpDownload -or -not (Test-Path $frankenPhp -PathType Leaf)) {
    $archive = Join-Path $env:TEMP "frankenphp-windows-$PID.zip"

    try {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
        Invoke-WebRequest 'https://github.com/php/frankenphp/releases/latest/download/frankenphp-windows-x86_64.zip' -OutFile $archive
        Expand-Archive -Path $archive -DestinationPath $InstallPath -Force
    } finally {
        Remove-Item $archive -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $frankenPhp -PathType Leaf) -or -not (Test-Path $php -PathType Leaf)) {
    throw 'The FrankenPHP Windows archive is incomplete.'
}

Invoke-CheckedCommand $frankenPhp @('version')

Write-Step 'Installing Microsoft SQL Server PHP drivers'
& (Join-Path $scriptPath 'Install-FrankenPhpSqlServerDrivers.ps1') `
    -InstallPath $InstallPath `
    -DriverVersion $SqlServerDriverVersion

Write-Step 'Installing PHP and Caddy configuration'
Copy-Item (Join-Path $scriptPath 'php.ini') $phpIni -Force
Copy-Item (Join-Path $scriptPath 'Caddyfile') $caddyFile -Force

$env:PHPRC = $phpIni
$env:FRANKENPHP_EXT_DIR = Join-Path $InstallPath 'ext'

Write-Step 'Configuring the Laravel production environment'
Set-DotEnvValue $envFile 'APP_ENV' 'production'
Set-DotEnvValue $envFile 'APP_DEBUG' 'false'
Set-DotEnvValue $envFile 'OCTANE_SERVER' 'frankenphp'
Set-DotEnvValue $envFile 'OCTANE_HTTPS' 'false'
Set-DotEnvValue $envFile 'OCTANE_GARBAGE' '50'
Set-DotEnvValue $envFile 'OCTANE_MAX_EXECUTION_TIME' '30'
Set-DotEnvValue $envFile 'LOG_API_REQUESTS' 'false'
Set-DotEnvValue $envFile 'LOG_DB_QUERIES' 'false'

Write-Step 'Installing application dependencies and caches'
$composer = Get-Command composer -ErrorAction SilentlyContinue
if (-not $composer) {
    throw 'Composer is not available in PATH.'
}

Push-Location $AppPath
try {
    Invoke-CheckedCommand $composer.Source @('install', '--no-dev', '--prefer-dist', '--optimize-autoloader', '--no-interaction')
    Invoke-CheckedCommand $php @('artisan', 'optimize:clear')
    Invoke-CheckedCommand $php @('artisan', 'optimize')

    if ($BuildAssets) {
        $pnpm = Get-Command pnpm -ErrorAction SilentlyContinue
        if (-not $pnpm) {
            throw 'pnpm is not available in PATH.'
        }

        Invoke-CheckedCommand $pnpm.Source @('install', '--frozen-lockfile')
        Invoke-CheckedCommand $pnpm.Source @('run', 'build')
    }
} finally {
    Pop-Location
}

Write-Step 'Validating the FrankenPHP runtime'
& (Join-Path $scriptPath 'Test-FrankenPhp.ps1') `
    -FrankenPhp $frankenPhp `
    -AppPath $AppPath `
    -PhpIni $phpIni

Write-Step 'Installing WinSW'
if (-not (Test-Path $serviceExecutable -PathType Leaf)) {
    Invoke-WebRequest 'https://github.com/winsw/winsw/releases/latest/download/WinSW-x64.exe' -OutFile $serviceExecutable
}

[xml] $serviceXml = Get-Content (Join-Path $scriptPath 'frankenphp-service.xml') -Raw
$serviceXml.service.workingdirectory = $AppPath

$serviceEnvironment = @{
    APP_ENV = 'production'
    APP_BASE_PATH = $AppPath
    APP_PUBLIC_PATH = (Join-Path $AppPath 'public')
    PHPRC = $phpIni
    FRANKENPHP_EXT_DIR = (Join-Path $InstallPath 'ext')
    LARAVEL_OCTANE = '1'
    MAX_REQUESTS = "$MaxRequests"
    REQUEST_MAX_EXECUTION_TIME = '30'
    OCTANE_WORKERS = "$Workers"
    OCTANE_ADMIN_PORT = "$AdminPort"
    OCTANE_PORT = "$Port"
}

foreach ($entry in $serviceXml.service.env) {
    if ($serviceEnvironment.ContainsKey($entry.name)) {
        $entry.value = $serviceEnvironment[$entry.name]
    }
}

$xmlSettings = [Xml.XmlWriterSettings]::new()
$xmlSettings.Indent = $true
$xmlSettings.Encoding = [Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create($serviceConfig, $xmlSettings)
try {
    $serviceXml.Save($writer)
} finally {
    $writer.Dispose()
}

if ($existingService) {
    Invoke-CheckedCommand $serviceExecutable @('uninstall')

    for ($attempt = 1; $attempt -le 15; $attempt++) {
        if (-not (Get-Service -Name $serviceId -ErrorAction SilentlyContinue)) {
            break
        }

        Start-Sleep -Seconds 1
    }

    if (Get-Service -Name $serviceId -ErrorAction SilentlyContinue) {
        throw 'The previous FrankenPHP service is still pending deletion. Wait briefly and run setup again.'
    }
}

foreach ($candidatePort in @($Port, $AdminPort)) {
    $listener = Get-NetTCPConnection -State Listen -LocalPort $candidatePort -ErrorAction SilentlyContinue
    if ($listener) {
        throw "TCP port $candidatePort is already in use by process $($listener[0].OwningProcess)."
    }
}

Invoke-CheckedCommand $serviceExecutable @('install')

if ($OpenFirewall) {
    Write-Step 'Configuring Windows Firewall'
    $firewallRule = Get-NetFirewallRule -DisplayName 'Fee Processor FrankenPHP' -ErrorAction SilentlyContinue
    if ($firewallRule) {
        Remove-NetFirewallRule -DisplayName 'Fee Processor FrankenPHP'
    }

    New-NetFirewallRule `
        -DisplayName 'Fee Processor FrankenPHP' `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort $Port | Out-Null
}

Write-Step 'Starting FrankenPHP'
Invoke-CheckedCommand $serviceExecutable @('start')

$healthUrl = "http://127.0.0.1:$Port/up"
$healthy = $false
for ($attempt = 1; $attempt -le 15; $attempt++) {
    Start-Sleep -Seconds 1

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            $healthy = $true
            break
        }
    } catch {
        Write-Verbose "Health attempt $attempt failed: $($_.Exception.Message)"
    }
}

if (-not $healthy) {
    throw "FrankenPHP started but did not become healthy at $healthUrl. Review logs in $InstallPath."
}

Write-Host "`nFrankenPHP and Laravel Octane are running at http://127.0.0.1:$Port" -ForegroundColor Green
Write-Host 'IIS was not modified and remains available for rollback.' -ForegroundColor Green
