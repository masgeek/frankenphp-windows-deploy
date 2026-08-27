[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $InstallPath = 'C:\FrankenPHP',
    [string] $SourcePath = (Join-Path $PSScriptRoot 'php.ini')
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
