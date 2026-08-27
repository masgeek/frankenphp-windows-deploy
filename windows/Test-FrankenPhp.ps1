param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $FrankenPhp = 'C:\FrankenPHP\frankenphp.exe',
    [string] $AppPath = 'C:\app',
    [string] $PhpIni = '',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1')
)

$ErrorActionPreference = 'Stop'
trap {
    Show-FrankenPhpError $_
    exit 1
}
if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [parameters]"
    Get-Help -Name $PSCommandPath -Full | Out-Host
    return
}
. (Join-Path $PSScriptRoot 'FrankenPhp-Helpers.ps1')

if (
    -not $PSBoundParameters.ContainsKey('AppPath') -and
    (Test-Path $ConfigPath -PathType Leaf)
) {
    $deploymentConfig = Import-PowerShellDataFile $ConfigPath
    if ($deploymentConfig.ContainsKey('AppPath')) {
        $AppPath = $deploymentConfig.AppPath
    }
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

if (-not (Test-Path $FrankenPhp -PathType Leaf)) {
    throw "FrankenPHP was not found at $FrankenPhp"
}

if (-not (Test-Path (Join-Path $AppPath 'artisan') -PathType Leaf)) {
    throw "Laravel was not found at $AppPath"
}

$installPath = Split-Path $FrankenPhp
$php = Join-Path $installPath 'php.exe'
if (-not $PhpIni) {
    $PhpIni = Join-Path $installPath 'php.ini'
}

if (-not (Test-Path $php -PathType Leaf)) {
    throw "The FrankenPHP PHP executable was not found at $php"
}

if (-not (Test-Path $PhpIni -PathType Leaf)) {
    throw "The FrankenPHP configuration was not found at $PhpIni"
}

$env:PHPRC = $PhpIni
$env:FRANKENPHP_EXT_DIR = Join-Path $installPath 'ext'
$modules = & $php -m
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to query FrankenPHP PHP modules.'
}

$requiredModules = @(
    'curl',
    'fileinfo',
    'intl',
    'mbstring',
    'openssl',
    'PDO',
    'pdo_pgsql',
    'pdo_sqlite',
    'pdo_sqlsrv',
    'redis',
    'sqlite3',
    'sqlsrv'
)

$missingModules = $requiredModules | Where-Object { $_ -notin $modules }
if ($missingModules.Count -gt 0) {
    throw "Missing required FrankenPHP modules: $($missingModules -join ', ')"
}

& $php (Join-Path $AppPath 'artisan') about
if ($LASTEXITCODE -ne 0) {
    throw 'Laravel failed to boot with FrankenPHP.'
}

[IO.File]::WriteAllText($appPathCache, $AppPath, [Text.UTF8Encoding]::new($false))

Write-Host 'FrankenPHP is ready for application smoke testing.' -ForegroundColor Green
