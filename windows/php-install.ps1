[CmdletBinding(SupportsShouldProcess)]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $BasePath = '',
    [string] $Version = '',
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\..\frankenphp-deploy.psd1'),
    [string] $SqlServerDriverVersion = '5.13.3',
    [string] $RedisExtensionVersion = '6.3.0',
    [string] $InstallSqlServer = 'Prompt',
    [string] $InstallRedis = 'Prompt',
    [string] $SetupCacert = 'Prompt',
    [string] $SystemPath = 'Prompt',
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$VerbosePreference = 'Continue'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-BasePath <path>] [-Version <version>] [-SystemPath Yes|No|Prompt] [-ForceDownload]"
    return
}

trap {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($Host.Name -notmatch 'ServerRemoteHost') { [void](Read-Host 'Press Enter to close') }
    exit 1
}

. (Join-Path $PSScriptRoot 'php-version-helpers.ps1')

$BasePath = Get-PhpBasePath -BasePath $BasePath

if (-not $Version) {
    $versionCachePath = Join-Path $PSScriptRoot '.php-versions.cache'
    $availableVersions = @()
    if (Test-Path $versionCachePath -PathType Leaf) {
        $raw = Get-Content $versionCachePath -Raw | ConvertFrom-Json
        $availableVersions = @($raw | ForEach-Object { $_.ToString() })
    }
    if ($availableVersions.Count -gt 0) {
        $installedVersions = @(Get-PhpInstalledVersions -BasePath $BasePath | ForEach-Object { $_.Version })
        Write-Host ''
        Write-Host '  Available PHP versions' -ForegroundColor Cyan
        Write-Host '  ----------------------' -ForegroundColor DarkGray
        $displayCount = [Math]::Min($availableVersions.Count, 20)
        $maxVersionLength = ($availableVersions | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
        for ($i = 0; $i -lt $displayCount; $i++) {
            $v = $availableVersions[$i].PadRight($maxVersionLength)
            $installed = if ($availableVersions[$i] -in $installedVersions) { ' [installed]' } else { '' }
            Write-Host "  [$($i + 1)] $v$installed"
        }
        if ($availableVersions.Count -gt 20) {
            Write-Host "  ... and $($availableVersions.Count - 20) more" -ForegroundColor DarkGray
        }
        Write-Host ''
        do {
            $choice = (Read-Host "Choose version [1]").Trim()
            if ([string]::IsNullOrWhiteSpace($choice)) { $choice = '1' }
            if ($choice -match '^\d+$' -and [int] $choice -ge 1 -and [int] $choice -le $displayCount) {
                $Version = $availableVersions[[int] $choice - 1]
            } else {
                $Version = $choice
            }
        } until ($Version -match '^\d+\.\d+\.\d+$')
    } else {
        Write-Verbose "No cached PHP versions found. Run Update-PhpVersionCatalog.ps1 to build the version list."
        do {
            $selectedVersion = (Read-Host "PHP version [$Version]").Trim()
            if (-not [string]::IsNullOrWhiteSpace($selectedVersion)) { $Version = $selectedVersion }
        } until ($Version -match '^\d+\.\d+\.\d+$')
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid PHP version '$Version'."
}

$installDir = Join-Path $BasePath $Version
$php = Join-Path $installDir 'php.exe'
$phpIni = Join-Path $installDir 'php.ini'
$archive = Join-Path $env:TEMP "php-$Version-nts-x64-$PID.zip"
$downloadUrls = @(
    "https://windows.php.net/downloads/releases/php-$Version-nts-Win32-vs17-x64.zip"
    "https://downloads.php.net/~windows/releases/php-$Version-nts-Win32-vs17-x64.zip"
    "https://downloads.php.net/~windows/releases/archives/php-$Version-nts-Win32-vs17-x64.zip"
)

foreach ($extensionChoice in @(
    @{ Name = 'InstallSqlServer'; Label = 'SQL Server drivers' },
    @{ Name = 'InstallRedis'; Label = 'Redis extension' },
    @{ Name = 'SetupCacert'; Label = 'CA certificate bundle (cacert.pem)' }
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

if ($PSCmdlet.ShouldProcess($installDir, "Install PHP $Version")) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null

    if ($ForceDownload -or -not (Test-Path $php -PathType Leaf)) {
        $downloaded = $false
        foreach ($downloadUrl in $downloadUrls) {
            try {
                Write-Verbose "Downloading PHP $Version from '$downloadUrl'."
                Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $archive
                $downloaded = $true
                break
            } catch {
                Write-Verbose "Download failed from '$downloadUrl': $($_.Exception.Message)"
            }
        }
        if (-not $downloaded) {
            throw "Unable to download PHP $Version from the configured release URLs."
        }
        Expand-Archive -Path $archive -DestinationPath $installDir -Force
        Remove-Item $archive -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path $php -PathType Leaf)) {
        throw "PHP $Version could not be found at $php after extraction."
    }

    $developmentIni = Join-Path $PSScriptRoot 'php.ini-development'
    if (-not (Test-Path $phpIni -PathType Leaf)) {
        Copy-Item $developmentIni $phpIni -Force
    }

    $iniContent = [IO.File]::ReadAllText($phpIni)
    $extensionDirectory = Join-Path $installDir 'ext'
    $iniContent = [Regex]::Replace(
        $iniContent,
        '(?m)^\s*;?\s*extension_dir\s*=.*$',
        "extension_dir = `"$extensionDirectory`""
    )
    [IO.File]::WriteAllText($phpIni, $iniContent, [Text.UTF8Encoding]::new($false))

    $env:PHPRC = $phpIni
    $env:FRANKENPHP_EXT_DIR = $installDir

    if ($InstallSqlServer -eq 'Yes') {
        Write-Verbose 'Installing Microsoft SQL Server PHP drivers.'
        & (Join-Path $PSScriptRoot 'internal\Install-FrankenPhpSqlServerDrivers.ps1') `
            -InstallPath $installDir `
            -DriverVersion $SqlServerDriverVersion
    }
    if ($InstallRedis -eq 'Yes') {
        Write-Verbose 'Installing the PHP Redis extension.'
        & (Join-Path $PSScriptRoot 'internal\Install-FrankenPhpRedisExtension.ps1') `
            -InstallPath $installDir `
            -ExtensionVersion $RedisExtensionVersion
    }
    if ($SetupCacert -eq 'Yes') {
        Write-Verbose 'Downloading CA certificate bundle.'
        $cacertDestination = Join-Path $installDir 'cacert.pem'
        if ($ForceDownload -or -not (Test-Path $cacertDestination -PathType Leaf)) {
            Invoke-WebRequest -UseBasicParsing -Uri 'https://curl.se/ca/cacert.pem' -OutFile $cacertDestination
        }
        $iniContent = [IO.File]::ReadAllText($phpIni)
        $cacertPath = $cacertDestination -replace '\\', '/'
        $iniContent = [Regex]::Replace(
            $iniContent,
            '(?m)^\s*;?\s*curl\.cainfo\s*=.*$',
            "curl.cainfo = `"$cacertPath`""
        )
        if ($iniContent -notmatch '(?m)^\s*curl\.cainfo\s*=') {
            $iniContent = $iniContent.TrimEnd() + "`ncurl.cainfo = `"$cacertPath`"`n"
        }
        $iniContent = [Regex]::Replace(
            $iniContent,
            '(?m)^\s*;?\s*openssl\.cafile\s*=.*$',
            "openssl.cafile = `"$cacertPath`""
        )
        if ($iniContent -notmatch '(?m)^\s*openssl\.cafile\s*=') {
            $iniContent = $iniContent.TrimEnd() + "`nopenssl.cafile = `"$cacertPath`"`n"
        }
        [IO.File]::WriteAllText($phpIni, $iniContent, [Text.UTF8Encoding]::new($false))
    }

    $versionOutput = & $php --version 2>$null
    if ($LASTEXITCODE -ne 0) { throw "PHP $Version was installed but could not be started." }
    Write-Host ($versionOutput | Select-Object -First 1) -ForegroundColor Green
    Write-Host "PHP $Version is installed at $installDir." -ForegroundColor Green

    $activeVersion = Get-PhpActiveVersion -BasePath $BasePath
    if (-not $activeVersion) {
        Write-Host "This is the first PHP version. Setting it as active." -ForegroundColor Cyan
        Set-PhpActiveVersion -Version $Version -BasePath $BasePath -SystemPath $SystemPath
    } else {
        Write-Host "Active PHP version is $activeVersion. Use 'php-use.ps1 -Version $Version' to switch." -ForegroundColor Yellow
    }
}
