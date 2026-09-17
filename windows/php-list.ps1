[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $BasePath = '',
    [switch] $Available
)

$ErrorActionPreference = 'Stop'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-BasePath <path>] [-Available]"
    Write-Host "  -Available  Show versions available for download from the PHP Windows catalog."
    return
}

. (Join-Path $PSScriptRoot 'php-version-helpers.ps1')

$BasePath = Get-PhpBasePath -BasePath $BasePath
$installedVersions = @(Get-PhpInstalledVersions -BasePath $BasePath)

Write-Host ''
Write-Host '  Installed PHP versions' -ForegroundColor Cyan
Write-Host '  ----------------------' -ForegroundColor DarkGray

if ($installedVersions.Count -eq 0) {
    Write-Host '  (none)' -ForegroundColor Gray
} else {
    $maxVersionLength = ($installedVersions | ForEach-Object { $_.Version.Length } | Measure-Object -Maximum).Maximum

    foreach ($v in $installedVersions) {
        $version = $v.Version.PadRight($maxVersionLength)
        if ($v.Active) {
            Write-Host "  * " -ForegroundColor Green -NoNewline
            Write-Host $version -ForegroundColor Green -NoNewline
            Write-Host '  active' -ForegroundColor DarkGreen
        } elseif ($v.Installed) {
            Write-Host "    $version"
        } else {
            Write-Host "    $version" -ForegroundColor Yellow -NoNewline
            Write-Host '  (incomplete)' -ForegroundColor DarkYellow
        }
    }
}

if ($Available) {
    $availableVersions = @(Get-PhpAvailableVersions -BasePath $BasePath)
    $installedNames = @($installedVersions | ForEach-Object { $_.Version })

    Write-Host ''
    Write-Host '  Available PHP versions' -ForegroundColor Cyan
    Write-Host '  ----------------------' -ForegroundColor DarkGray

    if ($availableVersions.Count -eq 0) {
        Write-Host '  (none — run Update-PhpVersionCatalog.ps1 to refresh)' -ForegroundColor Gray
    } else {
        $maxAvailLength = ($availableVersions | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
        $shown = 0
        foreach ($version in $availableVersions) {
            if ($shown -ge 25) {
                Write-Host "  ... and $($availableVersions.Count - $shown) more" -ForegroundColor DarkGray
                break
            }
            $versionPad = $version.PadRight($maxAvailLength)
            if ($version -in $installedNames) {
                Write-Host "    $versionPad" -NoNewline
                Write-Host '  [installed]' -ForegroundColor DarkGreen
            } else {
                Write-Host "    $versionPad"
            }
            $shown++
        }
    }
}

Write-Host ''
