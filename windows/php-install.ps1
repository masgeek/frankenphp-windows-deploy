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
    [string] $IniSource = '',
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$VerbosePreference = 'Continue'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-BasePath <path>] [-Version <version>] [-IniSource <path>] [-SystemPath Yes|No|Prompt] [-ForceDownload]"
    Write-Host "  -IniSource  Path to a custom php.ini file to use."
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
$isInstalled = Test-PhpVersionInstalled -Version $Version -BasePath $BasePath

if ($PSCmdlet.ShouldProcess($installDir, "Configure PHP $Version")) {
    if ($isInstalled) {
        Write-Host ''
        Write-Host "  PHP $Version is already installed at $installDir" -ForegroundColor Green
        Write-Host ''
        Write-Host '  What would you like to do?' -ForegroundColor Cyan
        Write-Host '  --------------------------' -ForegroundColor DarkGray
        Write-Host '  [1] Configure (php.ini + extensions)'
        Write-Host '  [2] Reinstall (download again + configure)'
        Write-Host '  [3] Skip'
        Write-Host ''
        do {
            $action = (Read-Host '  Choose action [1]').Trim()
            if ([string]::IsNullOrWhiteSpace($action)) { $action = '1' }
        } until ($action -in @('1', '2', '3'))

        if ($action -eq '3') {
            Write-Host '  Skipping.' -ForegroundColor DarkGray
            return
        }

        if ($action -eq '2') {
            Write-Host "  Downloading PHP $Version..." -ForegroundColor DarkGray
            New-Item -ItemType Directory -Path $installDir -Force | Out-Null
            $archive = Join-Path $env:TEMP "php-$Version-nts-x64-$PID.zip"
            $downloadUrls = @(
                "https://windows.php.net/downloads/releases/php-$Version-nts-Win32-vs17-x64.zip"
                "https://downloads.php.net/~windows/releases/php-$Version-nts-Win32-vs17-x64.zip"
                "https://downloads.php.net/~windows/releases/archives/php-$Version-nts-Win32-vs17-x64.zip"
            )
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
            if (-not (Test-Path $php -PathType Leaf)) {
                throw "PHP $Version could not be found at $php after extraction."
            }
        }
    } else {
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
        $archive = Join-Path $env:TEMP "php-$Version-nts-x64-$PID.zip"
        $downloadUrls = @(
            "https://windows.php.net/downloads/releases/php-$Version-nts-Win32-vs17-x64.zip"
            "https://downloads.php.net/~windows/releases/php-$Version-nts-Win32-vs17-x64.zip"
            "https://downloads.php.net/~windows/releases/archives/php-$Version-nts-Win32-vs17-x64.zip"
        )
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
    }

    Write-Host ''
    Write-Host '  PHP configuration' -ForegroundColor Cyan
    Write-Host '  -----------------' -ForegroundColor DarkGray

    $developmentIni = Join-Path $PSScriptRoot 'php.ini-development'
    $productionIni = Join-Path $PSScriptRoot 'php.ini'
    $hasExisting = Test-Path $phpIni -PathType Leaf

    if ($IniSource) {
        if (-not (Test-Path $IniSource -PathType Leaf)) {
            throw "Custom php.ini not found: $IniSource"
        }
        Copy-Item $IniSource $phpIni -Force
        Write-Host "  Copied $IniSource to $phpIni" -ForegroundColor DarkGray
    } else {
        if ($hasExisting) {
            Write-Host '  [1] Keep existing php.ini'
        }
        Write-Host "  [$(if ($hasExisting) { '2' } else { '1' })] Development (visible errors, OPcache off)"
        Write-Host "  [$(if ($hasExisting) { '3' } else { '2' })] Production (errors hidden, OPcache on)"
        Write-Host "  [$(if ($hasExisting) { '4' } else { '3' })] Custom file..."
        Write-Host ''
        do {
            $sourceChoice = (Read-Host "  Choose source [$(if ($hasExisting) { '1' } else { '1' })]").Trim()
            if ([string]::IsNullOrWhiteSpace($sourceChoice)) { $sourceChoice = if ($hasExisting) { '1' } else { '1' } }
        } until ($sourceChoice -match '^\d+$')

        $selectedSource = if ($hasExisting) {
            switch ($sourceChoice) {
                '1' { $phpIni }
                '2' { $developmentIni }
                '3' { $productionIni }
                '4' { (Read-Host '  Enter path to custom php.ini').Trim() }
            }
        } else {
            switch ($sourceChoice) {
                '1' { $developmentIni }
                '2' { $productionIni }
                '3' { (Read-Host '  Enter path to custom php.ini').Trim() }
            }
        }

        if (-not (Test-Path $selectedSource -PathType Leaf)) {
            throw "Source php.ini not found: $selectedSource"
        }
        if ($selectedSource -ne $phpIni) {
            Copy-Item $selectedSource $phpIni -Force
            Write-Host "  Copied to $phpIni" -ForegroundColor DarkGray
        }
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
            $answer = (Read-Host "  Install $($extensionChoice.Label)? [Y/n]").Trim().ToLowerInvariant()
            Set-Variable -Name $extensionChoice.Name -Value $(if ($answer -in @('n', 'no')) { 'No' } else { 'Yes' })
        }
    }

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
    if ($LASTEXITCODE -ne 0) { throw "PHP $Version could not be started." }
    Write-Host ''
    Write-Host "  $($versionOutput | Select-Object -First 1)" -ForegroundColor Green
    Write-Host "  Path: $installDir" -ForegroundColor DarkGray

    $activeVersion = Get-PhpActiveVersion -BasePath $BasePath
    if (-not $activeVersion -or $activeVersion -eq $Version) {
        if (-not $activeVersion) {
            Write-Host "  This is the first PHP version. Setting it as active." -ForegroundColor Cyan
        }
        Set-PhpActiveVersion -Version $Version -BasePath $BasePath -SystemPath $SystemPath
    } else {
        Write-Host "  Active PHP version is $activeVersion. Use 'php-use.ps1 -Version $Version' to switch." -ForegroundColor Yellow
    }
    Write-Host ''
}
