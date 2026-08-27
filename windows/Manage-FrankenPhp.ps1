[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1')
)

$ErrorActionPreference = 'Stop'

trap {
    Write-Host "`n  ERROR  $($_.Exception.Message)" -ForegroundColor Red
    [void](Read-Host '  Press Enter to close')
    exit 1
}

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-ConfigPath <path>]"
    Write-Host ''
    Write-Host 'Actions:'
    Write-Host '  1. Install or update FrankenPHP'
    Write-Host '  2. Publish FrankenPHP to PATH'
    Write-Host '  3. Test FrankenPHP runtime'
    Write-Host '  4. Copy and update php.ini'
    Write-Host '  5. Install SQL Server drivers'
    Write-Host '  6. Install Redis extension'
    Write-Host '  7. Set up Laravel application'
    Write-Host '  8. Install or update FrankenPHP service'
    Write-Host '  9. Uninstall FrankenPHP'
    Write-Host '  Q. Quit'
    return
}

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$installPath = 'C:\FrankenPHP'
if (Test-Path $ConfigPath -PathType Leaf) {
    $configuration = Import-PowerShellDataFile $ConfigPath
    if ($configuration.ContainsKey('InstallPath')) {
        $installPath = $configuration.InstallPath
    }
}
$installPath = [IO.Path]::GetFullPath($installPath).TrimEnd('\')

function Show-Menu {
    Write-Host ''
    Write-Host 'FrankenPHP Deployment' -ForegroundColor Cyan
    Write-Host "Install path: $installPath" -ForegroundColor DarkGray
    Write-Host '1. Install or update FrankenPHP'
    Write-Host '2. Publish FrankenPHP to PATH'
    Write-Host '3. Test FrankenPHP runtime'
    Write-Host '4. Copy and update php.ini'
    Write-Host '5. Install SQL Server drivers'
    Write-Host '6. Install Redis extension'
    Write-Host '7. Set up Laravel application'
    Write-Host '8. Install or update FrankenPHP service'
    Write-Host '9. Uninstall FrankenPHP'
    Write-Host 'Q. Quit'
    Write-Host ''
}

function Invoke-Action {
    param(
        [string] $Name,
        [string] $Script,
        [string[]] $Arguments = @()
    )

    $targetScript = Join-Path $scriptPath $Script
    Write-Host "`nRunning $Name..." -ForegroundColor Cyan
    $hostExecutable = if ($PSVersionTable.PSEdition -eq 'Core') {
        Join-Path $PSHOME 'pwsh.exe'
    } else {
        Join-Path $PSHOME 'powershell.exe'
    }
    $processArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $targetScript) + $Arguments
    $quotedArguments = $processArguments | ForEach-Object {
        if ($_ -match '^-') {
            $_
        } else {
            '"' + ([string] $_).Replace('"', '\"') + '"'
        }
    }
    $actionProcess = Start-Process -FilePath $hostExecutable -ArgumentList ($quotedArguments -join ' ') -WorkingDirectory $scriptPath -Wait -PassThru
    if ($actionProcess.ExitCode -ne 0) {
        throw "The action failed with exit code $($actionProcess.ExitCode)."
    }
}

while ($true) {
    Show-Menu

    $selection = (Read-Host 'Select an action').Trim().ToUpperInvariant()
    if ($selection -eq 'Q') {
        break
    }

    try {
        switch ($selection) {
            '1' { Invoke-Action 'Install or update FrankenPHP' 'Install-FrankenPhp.ps1' @('-InstallPath', $installPath, '-ConfigPath', $ConfigPath) }
            '2' { Invoke-Action 'Publish FrankenPHP to PATH' 'Set-FrankenPhpSystemPath.ps1' @('-InstallPath', $installPath) }
            '3' { Invoke-Action 'Test FrankenPHP runtime' 'Test-FrankenPhp.ps1' @('-FrankenPhp', (Join-Path $installPath 'frankenphp.exe'), '-ConfigPath', $ConfigPath) }
            '4' { Invoke-Action 'Copy and update php.ini' 'Update-FrankenPhpPhpIni.ps1' @('-InstallPath', $installPath) }
            '5' { Invoke-Action 'Install SQL Server drivers' 'Install-FrankenPhpSqlServerDrivers.ps1' @('-InstallPath', $installPath) }
            '6' { Invoke-Action 'Install Redis extension' 'Install-FrankenPhpRedisExtension.ps1' @('-InstallPath', $installPath) }
            '7' { Invoke-Action 'Set up Laravel application' 'Setup-FrankenPhp.ps1' @('-InstallPath', $installPath, '-ConfigPath', $ConfigPath) }
            '8' { Invoke-Action 'Install or update FrankenPHP service' 'Install-FrankenPhpService.ps1' @('-InstallPath', $installPath, '-ConfigPath', $ConfigPath) }
            '9' { Invoke-Action 'Uninstall FrankenPHP' 'Uninstall-FrankenPhp.ps1' @('-InstallPath', $installPath, '-ConfigPath', $ConfigPath) }
            default { throw 'Invalid selection. Choose a displayed number or Q.' }
        }
    } catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
    }

    Write-Host ''
    [void](Read-Host 'Press Enter to return to the menu')
}
