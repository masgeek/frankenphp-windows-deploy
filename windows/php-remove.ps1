[CmdletBinding(SupportsShouldProcess)]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $BasePath = '',
    [string] $Version = ''
)

$ErrorActionPreference = 'Stop'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) -Version <version> [-BasePath <path>] [-WhatIf]"
    return
}

. (Join-Path $PSScriptRoot 'php-version-helpers.ps1')

$BasePath = Get-PhpBasePath -BasePath $BasePath

if (-not $Version) {
    $installedVersions = @(Get-PhpInstalledVersions -BasePath $BasePath)
    if ($installedVersions.Count -eq 0) {
        throw "No PHP versions installed."
    }
    Write-Host 'Installed PHP versions:' -ForegroundColor Cyan
    for ($i = 0; $i -lt $installedVersions.Count; $i++) {
        $marker = if ($installedVersions[$i].Active) { ' *' } else { '' }
        Write-Host "  [$($i + 1)] $($installedVersions[$i].Version)$marker"
    }
    do {
        $choice = (Read-Host 'Choose version to remove').Trim()
    } until ($choice -match '^\d+$' -and [int] $choice -ge 1 -and [int] $choice -le $installedVersions.Count)
    $Version = $installedVersions[[int] $choice - 1].Version
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid PHP version format '$Version'. Expected format: X.Y.Z"
}

$versionDir = Join-Path $BasePath $Version
if (-not (Test-Path $versionDir -PathType Container)) {
    throw "PHP $Version is not installed at $versionDir."
}

$activeVersion = Get-PhpActiveVersion -BasePath $BasePath
if ($activeVersion -eq $Version) {
    throw "Cannot remove the active PHP version. Run 'php-use.ps1' to switch to another version first."
}

if ($PSCmdlet.ShouldProcess($versionDir, "Remove PHP $Version")) {
    Remove-Item -Path $versionDir -Recurse -Force
    Write-Host "Removed PHP $Version from $versionDir." -ForegroundColor Green
}
