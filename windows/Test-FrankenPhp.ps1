param(
    [string] $FrankenPhp = 'C:\Program Files\FrankenPHP\frankenphp.exe',
    [string] $AppPath = 'C:\fee-processor',
    [string] $PhpIni = ''
)

$ErrorActionPreference = 'Stop'

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
    'mbstring',
    'openssl',
    'PDO',
    'pdo_pgsql',
    'pdo_sqlsrv',
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

Write-Host 'FrankenPHP is ready for application smoke testing.' -ForegroundColor Green
