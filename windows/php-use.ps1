[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $BasePath = '',
    [string] $Version = '',
    [string] $SystemPath = 'Prompt'
)

$ErrorActionPreference = 'Stop'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) -Version <version> [-BasePath <path>] [-SystemPath Yes|No|Prompt]"
    Write-Host "  -SystemPath  Add to system PATH for Windows services (requires admin)."
    return
}

. (Join-Path $PSScriptRoot 'php-version-helpers.ps1')

$BasePath = Get-PhpBasePath -BasePath $BasePath

if (-not $Version) {
    $installedVersions = @(Get-PhpInstalledVersions -BasePath $BasePath)
    if ($installedVersions.Count -eq 0) {
        throw "No PHP versions installed. Run php-install.ps1 first."
    }
    Write-Host ''
    Write-Host '  Installed PHP versions' -ForegroundColor Cyan
    Write-Host '  ----------------------' -ForegroundColor DarkGray
    $maxVersionLength = ($installedVersions | ForEach-Object { $_.Version.Length } | Measure-Object -Maximum).Maximum
    for ($i = 0; $i -lt $installedVersions.Count; $i++) {
        $version = $installedVersions[$i].Version.PadRight($maxVersionLength)
        if ($installedVersions[$i].Active) {
            Write-Host "  [$($i + 1)] " -NoNewline
            Write-Host "$version " -ForegroundColor Green -NoNewline
            Write-Host '*' -ForegroundColor DarkGreen
        } else {
            Write-Host "  [$($i + 1)] $version"
        }
    }
    Write-Host ''
    do {
        $choice = (Read-Host '  Choose version').Trim()
    } until ($choice -match '^\d+$' -and [int] $choice -ge 1 -and [int] $choice -le $installedVersions.Count)
    $Version = $installedVersions[[int] $choice - 1].Version
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid PHP version format '$Version'. Expected format: X.Y.Z"
}

if (-not (Test-PhpVersionInstalled -Version $Version -BasePath $BasePath)) {
    throw "PHP $Version is not installed. Run php-install.ps1 -Version $Version first."
}

Set-PhpActiveVersion -Version $Version -BasePath $BasePath -SystemPath $SystemPath
Write-Host ''
Write-Host "  Switched to PHP $Version" -ForegroundColor Green
Write-Host "  Path: $(Join-Path $BasePath $Version)"
Write-Host ''
