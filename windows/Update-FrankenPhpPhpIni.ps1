[CmdletBinding()]
param(
    [string] $InstallPath = 'C:\Program Files\FrankenPHP',
    [string] $SourcePath = (Join-Path $PSScriptRoot 'php.ini')
)

$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session.'
}

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
