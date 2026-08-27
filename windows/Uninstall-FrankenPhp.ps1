[CmdletBinding(SupportsShouldProcess)]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $InstallPath = 'C:\FrankenPHP',
    [string] $ServiceName = 'laravel-frankenphp',
    [string] $FirewallRuleName = 'Laravel FrankenPHP',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [switch] $KeepInstallPath
)

$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'
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
Assert-FrankenPhpAdministrator

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

if (Test-Path $ConfigPath -PathType Leaf) {
    $deploymentConfig = Import-PowerShellDataFile $ConfigPath
    foreach ($parameterName in @('InstallPath', 'ServiceName', 'FirewallRuleName')) {
        if (-not $PSBoundParameters.ContainsKey($parameterName) -and $deploymentConfig.ContainsKey($parameterName)) {
            Set-Variable -Name $parameterName -Value $deploymentConfig[$parameterName]
        }
    }
}

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$installPathCache = Join-Path $scriptPath '.install-path.cache'
$appPathCache = Join-Path $scriptPath '.cache'
if (-not $PSBoundParameters.ContainsKey('InstallPath') -and
    -not ($deploymentConfig -and $deploymentConfig.ContainsKey('InstallPath')) -and
    (Test-Path $installPathCache -PathType Leaf)) {
    $cachedInstallPath = [IO.File]::ReadAllText($installPathCache).Trim()
    if (-not [string]::IsNullOrWhiteSpace($cachedInstallPath)) {
        $InstallPath = $cachedInstallPath
        Write-Verbose "Using cached FrankenPHP installation path '$InstallPath'."
    }
}

$InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
$extensionPath = Join-Path $InstallPath 'ext'
$legacyServiceExecutable = Join-Path $InstallPath 'frankenphp-service.exe'

if (Test-Path $appPathCache -PathType Leaf) {
    $cachedAppPathValue = [IO.File]::ReadAllText($appPathCache).Trim()
    if ($cachedAppPathValue) {
        $cachedAppPath = [IO.Path]::GetFullPath($cachedAppPathValue).TrimEnd('\')
        if ($cachedAppPath.Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove '$InstallPath' because it is also the cached application directory."
        }
    }
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Verbose "Found FrankenPHP service '$ServiceName' with status $($existingService.Status)."
    if ($existingService.Status -ne 'Stopped') {
        Write-Verbose "Stopping service '$ServiceName'."
        if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop FrankenPHP service')) {
            Stop-Service -Name $ServiceName -Force
            $existingService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
    }

    $servicePath = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").PathName
    if ($servicePath -and (Test-Path $legacyServiceExecutable -PathType Leaf) -and
        $servicePath.IndexOf($legacyServiceExecutable, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        $servyCommand = Get-Command servy-cli -ErrorAction SilentlyContinue
        if (-not $servyCommand) {
            throw 'The legacy FrankenPHP service was found, but servy-cli is not available in PATH.'
        }

        if ($PSCmdlet.ShouldProcess($ServiceName, 'Uninstall legacy FrankenPHP service')) {
            Write-Verbose "Uninstalling legacy service with $legacyServiceExecutable."
            Invoke-CheckedCommand $legacyServiceExecutable @('uninstall')
        }
    } else {
        $servyCommand = Get-Command servy-cli -ErrorAction SilentlyContinue
        if (-not $servyCommand) {
            throw 'FrankenPHP service was found, but servy-cli is not available in PATH.'
        }

        if ($PSCmdlet.ShouldProcess($ServiceName, 'Uninstall FrankenPHP service')) {
            Write-Verbose "Uninstalling service with $($servyCommand.Source)."
            Invoke-CheckedCommand $servyCommand.Source @('uninstall', "--name=$ServiceName", '--quiet')
        }
    }

    if (-not $WhatIfPreference) {
        for ($attempt = 1; $attempt -le 15; $attempt++) {
            if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
                break
            }

            Start-Sleep -Seconds 1
        }

        if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
            throw "The FrankenPHP service is still pending deletion. Wait briefly and run the script again."
        }
    }
} else {
    Write-Verbose "Service '$ServiceName' was not found."
    Write-Host 'The FrankenPHP service is not installed.' -ForegroundColor Yellow
}

$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
$pathEntries = @($machinePath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$updatedMachinePath = @($pathEntries | Where-Object {
    -not $_.Trim().TrimEnd('\').Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)
}) -join ';'
if ($PSCmdlet.ShouldProcess('Machine PATH', "Remove $InstallPath")) {
    Write-Verbose "Removing '$InstallPath' from the machine PATH."
    [Environment]::SetEnvironmentVariable('Path', $updatedMachinePath, 'Machine')
}

$processPathEntries = @($env:Path -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$updatedProcessPath = @($processPathEntries | Where-Object {
    -not $_.Trim().TrimEnd('\').Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)
}) -join ';'
$env:Path = $updatedProcessPath

$machineExtensionPath = [Environment]::GetEnvironmentVariable('FRANKENPHP_EXT_DIR', 'Machine')
if ($machineExtensionPath -and $machineExtensionPath.Trim().TrimEnd('\').Equals($extensionPath, [StringComparison]::OrdinalIgnoreCase)) {
    if ($PSCmdlet.ShouldProcess('Machine FRANKENPHP_EXT_DIR', 'Remove FrankenPHP environment variable')) {
        Write-Verbose 'Removing the machine FRANKENPHP_EXT_DIR variable.'
        [Environment]::SetEnvironmentVariable('FRANKENPHP_EXT_DIR', $null, 'Machine')
    }
}
if ($env:FRANKENPHP_EXT_DIR -and $env:FRANKENPHP_EXT_DIR.Trim().TrimEnd('\').Equals($extensionPath, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Verbose 'Removing the process FRANKENPHP_EXT_DIR variable.'
    Remove-Item Env:FRANKENPHP_EXT_DIR -ErrorAction SilentlyContinue
}

$firewallRule = Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue
if ($firewallRule -and $PSCmdlet.ShouldProcess($FirewallRuleName, 'Remove firewall rule')) {
    Write-Verbose "Removing firewall rule '$FirewallRuleName'."
    Remove-NetFirewallRule -DisplayName $FirewallRuleName
}

if (-not $KeepInstallPath -and (Test-Path $InstallPath)) {
    if ($PSCmdlet.ShouldProcess($InstallPath, 'Remove FrankenPHP installation')) {
        Write-Verbose "Removing FrankenPHP installation at '$InstallPath'."
        Remove-Item $InstallPath -Recurse -Force
    }
} else {
    Write-Verbose "Keeping FrankenPHP installation at '$InstallPath' because -KeepInstallPath was supplied."
}

Write-Host 'FrankenPHP was uninstalled.' -ForegroundColor Green
