[CmdletBinding(SupportsShouldProcess)]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $Runtime = '',
    [string] $InstallPath = '',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [switch] $KeepInstallPath
)

$ErrorActionPreference = 'Stop'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-Runtime <Php|FrankenPhp>] [-InstallPath <path>] [-KeepInstallPath]"
    return
}

trap {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($Host.Name -notmatch 'ServerRemoteHost') { [void](Read-Host 'Press Enter to close') }
    exit 1
}

if (-not $PSBoundParameters.ContainsKey('Runtime')) {
    Write-Host 'Select runtime to uninstall:' -ForegroundColor Cyan
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
    $frankenArguments = @('-ConfigPath', $ConfigPath, "-KeepInstallPath:$KeepInstallPath")
    if ($PSBoundParameters.ContainsKey('InstallPath')) {
        $frankenArguments += @('-InstallPath', $InstallPath)
    }
    & (Join-Path $PSScriptRoot 'internal\Uninstall-FrankenPhp.ps1') @frankenArguments
    exit $LASTEXITCODE
}

$phpInstallPathCache = Join-Path $PSScriptRoot '.php-install-path.cache'
if (-not $PSBoundParameters.ContainsKey('InstallPath') -and (Test-Path $phpInstallPathCache -PathType Leaf)) {
    $InstallPath = [IO.File]::ReadAllText($phpInstallPathCache).Trim()
}
if (-not $InstallPath) { $InstallPath = 'C:\PHP' }
$InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = @($userPath -split ';' | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_) -and
    -not $_.Trim().TrimEnd('\').Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)
})
if ($PSCmdlet.ShouldProcess('User PATH', "Remove $InstallPath")) {
    [Environment]::SetEnvironmentVariable('Path', ($pathEntries -join ';'), 'User')
    $env:Path = @($env:Path -split ';' | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        -not $_.Trim().TrimEnd('\').Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)
    }) -join ';'
}

if (-not $KeepInstallPath -and (Test-Path $InstallPath)) {
    if ($PSCmdlet.ShouldProcess($InstallPath, 'Remove PHP installation')) {
        Remove-Item $InstallPath -Recurse -Force
    }
}

Write-Host 'Regular PHP was uninstalled.' -ForegroundColor Green
