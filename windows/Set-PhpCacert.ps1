[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $Runtime = '',
    [string] $InstallPath = '',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$VerbosePreference = 'Continue'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-Runtime Php|FrankenPhp] [-InstallPath <path>] [-ForceDownload]"
    return
}

trap {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($Host.Name -notmatch 'ServerRemoteHost') { [void](Read-Host 'Press Enter to close') }
    exit 1
}

if (-not $PSBoundParameters.ContainsKey('Runtime')) {
    Write-Host 'Select runtime:' -ForegroundColor Cyan
    Write-Host '  [1] Regular PHP'
    Write-Host '  [2] FrankenPHP'
    do {
        $runtimeChoice = (Read-Host 'Choose runtime [1]').Trim()
        if ([string]::IsNullOrWhiteSpace($runtimeChoice)) { $runtimeChoice = '1' }
    } until ($runtimeChoice -in @('1', '2'))
    $Runtime = if ($runtimeChoice -eq '2') { 'FrankenPhp' } else { 'Php' }
}
if ($Runtime -notin @('Php', 'FrankenPhp')) {
    throw "Invalid runtime '$Runtime'. Choose Php or FrankenPhp."
}

if ($Runtime -eq 'FrankenPhp') {
    $frankenArguments = @('-ConfigPath', $ConfigPath, '-SetupCacert', 'Yes')
    if ($PSBoundParameters.ContainsKey('InstallPath')) {
        $frankenArguments += @('-InstallPath', $InstallPath)
    }
    if ($ForceDownload) { $frankenArguments += '-ForceDownload' }
    & (Join-Path $PSScriptRoot 'internal\Install-FrankenPhp.ps1') @frankenArguments
    exit $LASTEXITCODE
}

$phpInstallPathCache = Join-Path $PSScriptRoot '.php-install-path.cache'
if (-not $PSBoundParameters.ContainsKey('InstallPath') -and (Test-Path $phpInstallPathCache -PathType Leaf)) {
    $InstallPath = [IO.File]::ReadAllText($phpInstallPathCache).Trim()
}
if (-not $InstallPath) { $InstallPath = 'C:\PHP' }
$InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')

$phpIni = Join-Path $InstallPath 'php.ini'
if (-not (Test-Path $phpIni -PathType Leaf)) {
    throw "No php.ini found at $phpIni. Run Install-Php.ps1 first."
}

Write-Verbose "Downloading CA certificate bundle."
$cacertDestination = Join-Path $InstallPath 'cacert.pem'
if ($ForceDownload -or -not (Test-Path $cacertDestination -PathType Leaf)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://curl.se/ca/cacert.pem' -OutFile $cacertDestination
}

$iniContent = [IO.File]::ReadAllText($phpIni)
$cacertPath = $cacertDestination -replace '\\', '/'
$iniContent = [Regex]::Replace(
    $iniContent,
    '(?m)^\s*;?\s*curl\.cainfo\s*=.*$',
    "curl.cainfo = `"$cacertPath`""
)
if ($iniContent -notmatch '(?m)^\s*curl\.cainfo\s*=') {
    $iniContent = $iniContent.TrimEnd() + "`ncurl.cainfo = `"$cacertPath`"`n"
}
$iniContent = [Regex]::Replace(
    $iniContent,
    '(?m)^\s*;?\s*openssl\.cafile\s*=.*$',
    "openssl.cafile = `"$cacertPath`""
)
if ($iniContent -notmatch '(?m)^\s*openssl\.cafile\s*=') {
    $iniContent = $iniContent.TrimEnd() + "`nopenssl.cafile = `"$cacertPath`"`n"
}
[IO.File]::WriteAllText($phpIni, $iniContent, [Text.UTF8Encoding]::new($false))
Write-Host "CA certificate bundle installed at $cacertDestination and configured in php.ini." -ForegroundColor Green
