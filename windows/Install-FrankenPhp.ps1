[CmdletBinding()]
param(
    [string] $InstallPath = 'C:\FrankenPHP',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = `
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

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

$deploymentConfig = $null
if (Test-Path $ConfigPath -PathType Leaf) {
    $deploymentConfig = Import-PowerShellDataFile $ConfigPath
    if (-not $PSBoundParameters.ContainsKey('InstallPath') -and $deploymentConfig.ContainsKey('InstallPath')) {
        $InstallPath = $deploymentConfig.InstallPath
    }
}

$InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$installPathCache = Join-Path $scriptPath '.install-path.cache'
$frankenPhp = Join-Path $InstallPath 'frankenphp.exe'
$php = Join-Path $InstallPath 'php.exe'

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
[IO.File]::WriteAllText($installPathCache, $InstallPath, [Text.UTF8Encoding]::new($false))

if ($ForceDownload -or -not (Test-Path $frankenPhp -PathType Leaf) -or -not (Test-Path $php -PathType Leaf)) {
    $archive = Join-Path $env:TEMP "frankenphp-windows-$PID.zip"
    try {
        Write-Verbose "Downloading FrankenPHP to '$InstallPath'."
        Invoke-WebRequest `
            -UseBasicParsing `
            -Uri 'https://github.com/php/frankenphp/releases/latest/download/frankenphp-windows-x86_64.zip' `
            -OutFile $archive
        Expand-Archive -Path $archive -DestinationPath $InstallPath -Force
    } finally {
        Remove-Item $archive -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Verbose "FrankenPHP is already installed at '$InstallPath'."
}

if (-not (Test-Path $frankenPhp -PathType Leaf) -or -not (Test-Path $php -PathType Leaf)) {
    throw 'The FrankenPHP Windows archive is incomplete.'
}

Invoke-CheckedCommand $frankenPhp @('version')
& (Join-Path $scriptPath 'Set-FrankenPhpSystemPath.ps1') -InstallPath $InstallPath
Write-Host "FrankenPHP is installed at $InstallPath." -ForegroundColor Green
