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
    [switch] $Development,
    [switch] $OpenFirewall,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'

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

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session.'
}

$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$legacyServiceExecutable = Join-Path $InstallPath 'frankenphp-service.exe'
$serviceId = 'fee-processor-frankenphp'
$servyCommand = Get-Command servy-cli -ErrorAction SilentlyContinue
if (-not $servyCommand) {
    throw 'Servy must be installed separately and servy-cli must be available in PATH.'
}

$servyCli = $servyCommand.Source

if ($Uninstall) {
    $existingService = Get-Service -Name $serviceId -ErrorAction SilentlyContinue
    if (-not $existingService) {
        Write-Host 'The FrankenPHP service is not installed.' -ForegroundColor Yellow

        return
    }

    if ($existingService.Status -ne 'Stopped') {
        Stop-Service -Name $serviceId -Force
        $existingService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    $servicePath = (Get-CimInstance Win32_Service -Filter "Name='$serviceId'").PathName
    if (
        (Test-Path $legacyServiceExecutable -PathType Leaf) -and
        $servicePath -and
        $servicePath.IndexOf($legacyServiceExecutable, [StringComparison]::OrdinalIgnoreCase) -ge 0
    ) {
        Invoke-CheckedCommand $legacyServiceExecutable @('uninstall')
    } else {
        Invoke-CheckedCommand $servyCli @('uninstall', "--name=$serviceId", '--quiet')
    }

    Write-Host 'The FrankenPHP service was uninstalled.' -ForegroundColor Green

    return
}

if ($Port -eq $AdminPort) {
    throw 'The public and admin ports must be different.'
}

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$appPathCache = Join-Path $scriptPath '.cache'
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
$artisan = Join-Path $AppPath 'artisan'
$envFile = Join-Path $AppPath '.env'
$frankenPhp = Join-Path $InstallPath 'frankenphp.exe'
$phpIni = Join-Path $InstallPath 'php.ini'
$caddyFile = Join-Path $InstallPath 'Caddyfile'

foreach ($requiredFile in @($artisan, $envFile, $frankenPhp, $phpIni, $caddyFile)) {
    if (-not (Test-Path $requiredFile -PathType Leaf)) {
        throw "Required file was not found: $requiredFile"
    }
}

Invoke-CheckedCommand $servyCli @('version')
[IO.File]::WriteAllText($appPathCache, $AppPath, [Text.UTF8Encoding]::new($false))

$existingService = Get-Service -Name $serviceId -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne 'Stopped') {
    Stop-Service -Name $serviceId -Force
    $existingService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

if ($existingService -and (Test-Path $legacyServiceExecutable -PathType Leaf)) {
    $servicePath = (Get-CimInstance Win32_Service -Filter "Name='$serviceId'").PathName
    if ($servicePath -and $servicePath.IndexOf($legacyServiceExecutable, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        Invoke-CheckedCommand $legacyServiceExecutable @('uninstall')

        for ($attempt = 1; $attempt -le 15; $attempt++) {
            if (-not (Get-Service -Name $serviceId -ErrorAction SilentlyContinue)) {
                break
            }

            Start-Sleep -Seconds 1
        }

        if (Get-Service -Name $serviceId -ErrorAction SilentlyContinue) {
            throw 'The legacy FrankenPHP service is still pending deletion. Wait briefly and run the script again.'
        }
    }
}

foreach ($candidatePort in @($Port, $AdminPort)) {
    $listener = Get-NetTCPConnection -State Listen -LocalPort $candidatePort -ErrorAction SilentlyContinue
    if ($listener) {
        throw "TCP port $candidatePort is already in use by process $($listener[0].OwningProcess)."
    }
}

$logPath = Join-Path $InstallPath 'logs'
New-Item -ItemType Directory -Path $logPath -Force | Out-Null
$appEnvironment = if ($Development) { 'local' } else { 'production' }
$appDebug = if ($Development) { 'true' } else { 'false' }
$serviceEnvironment = @(
    "APP_ENV=$appEnvironment"
    "APP_DEBUG=$appDebug"
    "APP_BASE_PATH=$AppPath"
    "APP_PUBLIC_PATH=$(Join-Path $AppPath 'public')"
    "PHPRC=$phpIni"
    "FRANKENPHP_EXT_DIR=$(Join-Path $InstallPath 'ext')"
    'LARAVEL_OCTANE=1'
    "MAX_REQUESTS=$MaxRequests"
    'REQUEST_MAX_EXECUTION_TIME=30'
    "OCTANE_WORKERS=$Workers"
    "OCTANE_ADMIN_PORT=$AdminPort"
    "OCTANE_PORT=$Port"
) -join ';'

$env:SERVY_PROCESS_PARAMETERS = "run --config `"$caddyFile`""
$env:SERVY_ENVIRONMENT_VARIABLES = $serviceEnvironment
try {
    Invoke-CheckedCommand $servyCli @(
        'install',
        "--name=$serviceId",
        '--displayName=Fee Processor - FrankenPHP',
        '--description=FrankenPHP and Laravel Octane service for the fee processor API.',
        "--path=$frankenPhp",
        "--startupDir=$AppPath",
        '--startupType=Automatic',
        '--priority=Normal',
        '--startTimeout=30',
        '--stopTimeout=30',
        "--stdout=$(Join-Path $logPath 'frankenphp-stdout.log')",
        "--stderr=$(Join-Path $logPath 'frankenphp-stderr.log')",
        '--enableSizeRotation',
        '--rotationSize=10',
        '--maxRotations=8',
        '--enableHealth',
        '--heartbeatInterval=10',
        '--maxFailedChecks=3',
        '--recoveryAction=RestartProcess',
        '--recoveryOnCleanExit',
        '--maxRestartAttempts=5',
        '--quiet'
    )
} finally {
    Remove-Item Env:SERVY_PROCESS_PARAMETERS -ErrorAction SilentlyContinue
    Remove-Item Env:SERVY_ENVIRONMENT_VARIABLES -ErrorAction SilentlyContinue
}

if ($OpenFirewall) {
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

Invoke-CheckedCommand $servyCli @('start', "--name=$serviceId", '--quiet')

$healthUrl = "http://127.0.0.1:$Port/up"
for ($attempt = 1; $attempt -le 15; $attempt++) {
    Start-Sleep -Seconds 1

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            Write-Host "FrankenPHP service is installed and running at http://127.0.0.1:$Port" -ForegroundColor Green

            return
        }
    } catch {
        Write-Verbose "Health attempt $attempt failed: $($_.Exception.Message)"
    }
}

throw "FrankenPHP started but did not become healthy at $healthUrl. Review logs in $logPath."
