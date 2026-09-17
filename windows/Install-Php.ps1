[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $Runtime = '',
    [string] $InstallPath = '',
    [string] $PhpVersion = '',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [string] $SqlServerDriverVersion = '5.13.3',
    [string] $RedisExtensionVersion = '6.3.0',
    [string] $InstallSqlServer = 'Prompt',
    [string] $InstallRedis = 'Prompt',
    [string] $SetupCacert = 'Prompt',
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$VerbosePreference = 'Continue'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-Runtime Php|FrankenPhp] [-InstallPath <path>] [-PhpVersion <version>]"
    Write-Host 'Run without -Runtime to choose Regular PHP or FrankenPHP interactively.'
    return
}

trap {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($Host.Name -notmatch 'ServerRemoteHost') { [void](Read-Host 'Press Enter to close') }
    exit 1
}

. (Join-Path $PSScriptRoot 'php-version-helpers.ps1')

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
    $frankenArguments = @('-ConfigPath', $ConfigPath)
    if ($PSBoundParameters.ContainsKey('InstallPath')) {
        $frankenArguments += @('-InstallPath', $InstallPath)
    }
    if ($SetupCacert -notin @('Prompt', '')) { $frankenArguments += @('-SetupCacert', $SetupCacert) }
    if ($ForceDownload) { $frankenArguments += '-ForceDownload' }
    & (Join-Path $PSScriptRoot 'internal\Install-FrankenPhp.ps1') @frankenArguments
    exit $LASTEXITCODE
}

$phpArguments = @()
if ($PSBoundParameters.ContainsKey('InstallPath')) { $phpArguments += @('-BasePath', $InstallPath) }
if ($PSBoundParameters.ContainsKey('PhpVersion')) { $phpArguments += @('-Version', $PhpVersion) }
if ($InstallSqlServer -notin @('Prompt', '')) { $phpArguments += @('-InstallSqlServer', $InstallSqlServer) }
if ($InstallRedis -notin @('Prompt', '')) { $phpArguments += @('-InstallRedis', $InstallRedis) }
if ($SetupCacert -notin @('Prompt', '')) { $phpArguments += @('-SetupCacert', $SetupCacert) }
if ($ForceDownload) { $phpArguments += '-ForceDownload' }
& (Join-Path $PSScriptRoot 'php-install.ps1') @phpArguments
