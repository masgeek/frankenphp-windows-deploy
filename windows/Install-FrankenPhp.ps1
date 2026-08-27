[CmdletBinding(SupportsShouldProcess)]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $InstallPath = 'C:\FrankenPHP',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [string] $Version = 'latest',
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string] $ExpectedSha256 = '',
    [string] $LogPath = '',
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
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

$deploymentConfig = $null
if (Test-Path $ConfigPath -PathType Leaf) {
    Write-Verbose "Loading deployment configuration from '$ConfigPath'."
    $deploymentConfig = Import-PowerShellDataFile $ConfigPath
    if (-not $PSBoundParameters.ContainsKey('InstallPath') -and $deploymentConfig.ContainsKey('InstallPath')) {
        $InstallPath = $deploymentConfig.InstallPath
    }
    if (-not $PSBoundParameters.ContainsKey('Version') -and $deploymentConfig.ContainsKey('FrankenPhpVersion')) {
        $Version = $deploymentConfig.FrankenPhpVersion
    }
    if (-not $PSBoundParameters.ContainsKey('ExpectedSha256') -and $deploymentConfig.ContainsKey('FrankenPhpSha256')) {
        $ExpectedSha256 = $deploymentConfig.FrankenPhpSha256
    }
    if (-not $PSBoundParameters.ContainsKey('LogPath') -and $deploymentConfig.ContainsKey('LogPath')) {
        $LogPath = $deploymentConfig.LogPath
    }
}

$InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
Write-Verbose "Using FrankenPHP installation path '$InstallPath'."
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$installPathCache = Join-Path $scriptPath '.install-path.cache'
$frankenPhp = Join-Path $InstallPath 'frankenphp.exe'
$php = Join-Path $InstallPath 'php.exe'

if ($Version -ne 'latest' -and $Version -notmatch '^v?\d+\.\d+\.\d+$') {
    throw "Invalid FrankenPHP version '$Version'. Use 'latest' or a version such as '1.5.0'."
}
if ($ExpectedSha256 -and $ExpectedSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
    throw 'FrankenPhpSha256 must be a 64-character SHA-256 value.'
}

$transcriptStarted = $false
if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = [IO.Path]::GetFullPath($LogPath)
    if ($PSCmdlet.ShouldProcess($LogPath, 'Create installer log')) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null
        Start-Transcript -Path $LogPath -Append | Out-Null
        $transcriptStarted = $true
    }
}

if ($WhatIfPreference) {
    Write-Verbose "WhatIf: FrankenPHP would be installed at '$InstallPath'."
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
    return
}

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
[IO.File]::WriteAllText($installPathCache, $InstallPath, [Text.UTF8Encoding]::new($false))
Write-Verbose "Cached the FrankenPHP installation path in '$installPathCache'."

if ($ForceDownload -or -not (Test-Path $frankenPhp -PathType Leaf) -or -not (Test-Path $php -PathType Leaf)) {
    $archive = Join-Path $env:TEMP "frankenphp-windows-$PID.zip"
    try {
        $downloadPath = if ($Version -eq 'latest') { 'latest/download' } else { "download/v$($Version.TrimStart('v'))" }
        $downloadUrl = "https://github.com/php/frankenphp/releases/$downloadPath/frankenphp-windows-x86_64.zip"
        Write-Verbose "Downloading FrankenPHP $Version from '$downloadUrl'."
        Invoke-WebRequest `
            -UseBasicParsing `
            -Uri $downloadUrl `
            -OutFile $archive
        $actualSha256 = (Get-FileHash -Path $archive -Algorithm SHA256).Hash
        Write-Verbose "Downloaded archive SHA-256: $actualSha256"
        if ($ExpectedSha256 -and $actualSha256 -ne $ExpectedSha256.ToUpperInvariant()) {
            throw "FrankenPHP archive checksum mismatch. Expected $ExpectedSha256, got $actualSha256."
        }
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
Write-Verbose 'Publishing FrankenPHP to the system PATH.'
& (Join-Path $scriptPath 'Set-FrankenPhpSystemPath.ps1') -InstallPath $InstallPath
Write-Host "FrankenPHP is installed at $InstallPath." -ForegroundColor Green
if ($transcriptStarted) { Stop-Transcript | Out-Null }
