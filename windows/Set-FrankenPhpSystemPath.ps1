[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $InstallPath = 'C:\FrankenPHP'
)

$ErrorActionPreference = 'Stop'
if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [parameters]"
    Get-Help -Name $PSCommandPath -Full | Out-Host
    return
}
. (Join-Path $PSScriptRoot 'FrankenPhp-Helpers.ps1')
Assert-FrankenPhpAdministrator -BoundParameters $PSBoundParameters -UnboundArguments $args -ScriptPath $PSCommandPath

$InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
$php = Join-Path $InstallPath 'php.exe'
if (-not (Test-Path $php -PathType Leaf)) {
    throw "FrankenPHP PHP was not found at $php"
}

$extensionPath = Join-Path $InstallPath 'ext'
if (-not (Test-Path $extensionPath -PathType Container)) {
    throw "FrankenPHP extensions were not found at $extensionPath"
}

$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
$pathEntries = @($machinePath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$otherPathEntries = @($pathEntries | Where-Object {
    -not $_.Trim().TrimEnd('\').Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)
})
$updatedMachinePath = (@($InstallPath) + $otherPathEntries) -join ';'

if (-not [string]::Equals($machinePath, $updatedMachinePath, [StringComparison]::Ordinal)) {
    [Environment]::SetEnvironmentVariable('Path', $updatedMachinePath, 'Machine')
}

$processPathEntries = @($env:Path -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$otherProcessPathEntries = @($processPathEntries | Where-Object {
    -not $_.Trim().TrimEnd('\').Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)
})
$env:Path = (@($InstallPath) + $otherProcessPathEntries) -join ';'

[Environment]::SetEnvironmentVariable('FRANKENPHP_EXT_DIR', $extensionPath, 'Machine')
$env:FRANKENPHP_EXT_DIR = $extensionPath

Write-Host "FrankenPHP PHP is available from the system PATH: $php" -ForegroundColor Green
