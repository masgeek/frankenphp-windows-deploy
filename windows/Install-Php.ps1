[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $Runtime = '',
    [string] $InstallPath = '',
    [string] $PhpVersion = '8.4.12',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [string] $SqlServerDriverVersion = '5.13.3',
    [string] $RedisExtensionVersion = '6.3.0',
    [string] $InstallSqlServer = 'Prompt',
    [string] $InstallRedis = 'Prompt',
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$VerbosePreference = 'Continue'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-InstallPath <path>] [-PhpVersion <version>] [-ForceDownload]"
    Write-Host 'Run without -Runtime to choose Regular PHP or FrankenPHP interactively.'
    return
}

trap {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($Host.Name -notmatch 'ServerRemoteHost') { [void](Read-Host 'Press Enter to close') }
    exit 1
}

if (-not $PSBoundParameters.ContainsKey('Runtime')) {
    Write-Host 'Select runtime:' -ForegroundColor Cyan
    Write-Host '  [1] Regular PHP'
    Write-Host '  [2] FrankenPHP'
    do {
        $runtimeChoice = (Read-Host 'Choose runtime [1]').Trim()
        if ([string]::IsNullOrWhiteSpace($runtimeChoice)) { $runtimeChoice = '1' }
    } until ($runtimeChoice -in @('1', '2'))
    $Runtime = if ($runtimeChoice -eq '2') { 'FrankenPhp' } else { 'Php' }
}
if ($Runtime -notin @('Php', 'FrankenPhp')) {
    throw "Invalid runtime '$Runtime'. Choose Php or FrankenPhp."
}
if ($Runtime -eq 'FrankenPhp') {
    $frankenArguments = @('-ConfigPath', $ConfigPath)
    if ($PSBoundParameters.ContainsKey('InstallPath')) {
        $frankenArguments += @('-InstallPath', $InstallPath)
    }
    if ($ForceDownload) { $frankenArguments += '-ForceDownload' }
    & (Join-Path $PSScriptRoot 'internal\Install-FrankenPhp.ps1') @frankenArguments
    exit $LASTEXITCODE
}

foreach ($extensionChoice in @(
    @{ Name = 'InstallSqlServer'; Label = 'SQL Server drivers' },
    @{ Name = 'InstallRedis'; Label = 'Redis extension' }
)) {
    $choice = Get-Variable -Name $extensionChoice.Name -ValueOnly
    if ($choice -notin @('Prompt', 'Yes', 'No')) {
        throw "Invalid $($extensionChoice.Name) value '$choice'. Use Prompt, Yes, or No."
    }
    if ($choice -eq 'Prompt') {
        $answer = (Read-Host "Install $($extensionChoice.Label)? [Y/n]").Trim().ToLowerInvariant()
        Set-Variable -Name $extensionChoice.Name -Value $(if ($answer -in @('n', 'no')) { 'No' } else { 'Yes' })
    }
}

$phpInstallPathCache = Join-Path $PSScriptRoot '.php-install-path.cache'
if (-not $PSBoundParameters.ContainsKey('InstallPath')) {
    if (Test-Path $phpInstallPathCache -PathType Leaf) {
        $cachedInstallPath = [IO.File]::ReadAllText($phpInstallPathCache).Trim()
        if ($cachedInstallPath) { $InstallPath = $cachedInstallPath }
    }
    if (-not $InstallPath) { $InstallPath = 'C:\PHP' }
}
$InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
[IO.File]::WriteAllText($phpInstallPathCache, $InstallPath, [Text.UTF8Encoding]::new($false))
if (-not $PSBoundParameters.ContainsKey('PhpVersion')) {
    $versionCachePath = Join-Path $PSScriptRoot '.php-versions.cache'
    $availableVersions = if (Test-Path $versionCachePath -PathType Leaf) {
        @(Get-Content $versionCachePath -Raw | ConvertFrom-Json)
    } else { @() }
    if ($availableVersions.Count -gt 0) {
        Write-Host 'Available PHP versions:' -ForegroundColor Cyan
        for ($index = 0; $index -lt [Math]::Min($availableVersions.Count, 20); $index++) {
            Write-Host "  [$($index + 1)] $($availableVersions[$index])"
        }
        do {
            $versionChoice = (Read-Host "Choose version [1]").Trim()
            if ([string]::IsNullOrWhiteSpace($versionChoice)) { $versionChoice = '1' }
            if ($versionChoice -match '^\d+$' -and [int] $versionChoice -ge 1 -and [int] $versionChoice -le [Math]::Min($availableVersions.Count, 20)) {
                $PhpVersion = $availableVersions[[int] $versionChoice - 1]
            } else {
                $PhpVersion = $versionChoice
            }
        } until ($PhpVersion -match '^\d+\.\d+\.\d+$')
    } else {
        Write-Verbose "No cached PHP versions found. Run Update-PhpVersionCatalog.ps1 to build the version list."
        do {
            $selectedVersion = (Read-Host "PHP version [$PhpVersion]").Trim()
            if (-not [string]::IsNullOrWhiteSpace($selectedVersion)) { $PhpVersion = $selectedVersion }
        } until ($PhpVersion -match '^\d+\.\d+\.\d+$')
    }
}
if ($PhpVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid PHP version '$PhpVersion'."
}

$php = Join-Path $InstallPath 'php.exe'
$phpIni = Join-Path $InstallPath 'php.ini'
$archive = Join-Path $env:TEMP "php-$PhpVersion-nts-x64-$PID.zip"
$downloadUrls = @(
    "https://windows.php.net/downloads/releases/php-$PhpVersion-nts-Win32-vs17-x64.zip"
    "https://downloads.php.net/~windows/releases/archives/php-$PhpVersion-nts-Win32-vs17-x64.zip"
)

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
try {
    if ($ForceDownload -or -not (Test-Path $php -PathType Leaf)) {
        $downloaded = $false
        foreach ($downloadUrl in $downloadUrls) {
            try {
                Write-Verbose "Downloading PHP $PhpVersion from '$downloadUrl'."
                Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $archive
                $downloaded = $true
                break
            } catch {
                Write-Verbose "Download failed from '$downloadUrl': $($_.Exception.Message)"
            }
        }
        if (-not $downloaded) {
            throw "Unable to download PHP $PhpVersion from the configured release URLs."
        }
        Expand-Archive -Path $archive -DestinationPath $InstallPath -Force
    }
} finally {
    Remove-Item $archive -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $php -PathType Leaf)) {
    throw "PHP was not found at $php"
}

if (-not (Test-Path $phpIni -PathType Leaf)) {
    $developmentIni = Join-Path $PSScriptRoot 'php.ini-development'
    if (-not (Test-Path $developmentIni -PathType Leaf)) {
        throw "PHP development configuration was not found: $developmentIni"
    }
    Copy-Item $developmentIni $phpIni -Force
}

$iniContent = [IO.File]::ReadAllText($phpIni)
$extensionDirectory = Join-Path $InstallPath 'ext'
$iniContent = [Regex]::Replace(
    $iniContent,
    '(?m)^\s*;?\s*extension_dir\s*=.*$',
    "extension_dir = `"$extensionDirectory`""
)
[IO.File]::WriteAllText($phpIni, $iniContent, [Text.UTF8Encoding]::new($false))

$env:PHPRC = $phpIni
$env:FRANKENPHP_EXT_DIR = $InstallPath
if ($InstallSqlServer -eq 'Yes') {
    Write-Verbose 'Installing Microsoft SQL Server PHP drivers.'
    & (Join-Path $PSScriptRoot 'internal\Install-FrankenPhpSqlServerDrivers.ps1') `
        -InstallPath $InstallPath `
        -DriverVersion $SqlServerDriverVersion
}
if ($InstallRedis -eq 'Yes') {
    Write-Verbose 'Installing the PHP Redis extension.'
    & (Join-Path $PSScriptRoot 'internal\Install-FrankenPhpRedisExtension.ps1') `
        -InstallPath $InstallPath `
        -ExtensionVersion $RedisExtensionVersion
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$pathEntries = @($pathEntries | Where-Object {
    -not $_.Trim().TrimEnd('\').Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)
})
[Environment]::SetEnvironmentVariable('Path', (@($InstallPath) + $pathEntries) -join ';', 'User')

$processPath = @($env:Path -split ';' | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_) -and
    -not $_.Trim().TrimEnd('\').Equals($InstallPath, [StringComparison]::OrdinalIgnoreCase)
})
$env:Path = (@($InstallPath) + $processPath) -join ';'

$versionOutput = & $php --version
if ($LASTEXITCODE -ne 0) { throw 'PHP was installed but could not be started.' }
Write-Host ($versionOutput | Select-Object -First 1) -ForegroundColor Green
Write-Host "PHP is installed at $InstallPath with SQL Server and Redis extensions, and is available in the user PATH." -ForegroundColor Green
