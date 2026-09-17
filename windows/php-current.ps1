[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $BasePath = ''
)

$ErrorActionPreference = 'Stop'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-BasePath <path>]"
    return
}

. (Join-Path $PSScriptRoot 'php-version-helpers.ps1')

$BasePath = Get-PhpBasePath -BasePath $BasePath
$activeVersion = Get-PhpActiveVersion -BasePath $BasePath

if ($activeVersion) {
    $versionDir = Join-Path $BasePath $activeVersion
    Write-Host "Active PHP version: $activeVersion" -ForegroundColor Green
    Write-Host "Path: $versionDir"
    $php = Join-Path $versionDir 'php.exe'
    if (Test-Path $php -PathType Leaf) {
        $versionOutput = & $php --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host ($versionOutput | Select-Object -First 1)
        }
    }
} else {
    Write-Host 'No active PHP version configured.' -ForegroundColor Yellow
    Write-Host "Use 'php-use.ps1' to select a version."
}
