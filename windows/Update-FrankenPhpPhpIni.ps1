[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $InstallPath = 'C:\FrankenPHP',
    [ValidateSet('Production', 'Development')]
    [string] $Environment = 'Production',
    [string] $SourcePath = ''
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
$VerbosePreference = 'Continue'

$InstallPath = [IO.Path]::GetFullPath($InstallPath)
if (-not $PSBoundParameters.ContainsKey('Environment')) {
    Write-Host 'Select PHP configuration:' -ForegroundColor Cyan
    Write-Host '  [P] Production'
    Write-Host '  [D] Development'
    do {
        $environmentChoice = (Read-Host 'Choose configuration [P]').Trim().ToUpperInvariant()
        if ([string]::IsNullOrWhiteSpace($environmentChoice)) {
            $environmentChoice = 'P'
        }
    } until ($environmentChoice -in @('P', 'D'))

    $Environment = if ($environmentChoice -eq 'D') { 'Development' } else { 'Production' }
}
if (-not $PSBoundParameters.ContainsKey('SourcePath')) {
    $sourceFileName = if ($Environment -eq 'Development') { 'php.ini-development' } else { 'php.ini' }
    $SourcePath = Join-Path $PSScriptRoot $sourceFileName
}
$SourcePath = [IO.Path]::GetFullPath($SourcePath)
$destination = Join-Path $InstallPath 'php.ini'

Write-Verbose "Source PHP configuration: $SourcePath"
Write-Verbose "Destination PHP configuration: $destination"

if (-not (Test-Path $SourcePath -PathType Leaf)) {
    throw "The source PHP configuration was not found: $SourcePath"
}

if (-not (Test-Path $InstallPath -PathType Container)) {
    throw "The FrankenPHP installation directory was not found: $InstallPath"
}

Copy-Item $SourcePath $destination -Force
(Get-Item $destination).LastWriteTime = Get-Date
Write-Verbose "Updated destination timestamp: $((Get-Item $destination).LastWriteTime)"

Write-Host "Updated PHP configuration: $destination" -ForegroundColor Green
