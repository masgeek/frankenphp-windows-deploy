function Get-PhpBasePath {
    [CmdletBinding()]
    param(
        [string] $BasePath = ''
    )

    if (-not $BasePath) {
        $phpInstallPathCache = Join-Path $PSScriptRoot '.php-install-path.cache'
        if (Test-Path $phpInstallPathCache -PathType Leaf) {
            $BasePath = [IO.File]::ReadAllText($phpInstallPathCache).Trim()
        }
        if (-not $BasePath) { $BasePath = 'C:\PHP' }
    }
    [IO.Path]::GetFullPath($BasePath).TrimEnd('\')
}

function Get-PhpActiveVersionFile {
    [CmdletBinding()]
    param(
        [string] $BasePath
    )
    Join-Path $BasePath '.php-active-version'
}

function Get-PhpActiveVersion {
    [CmdletBinding()]
    param(
        [string] $BasePath = ''
    )

    $BasePath = Get-PhpBasePath -BasePath $BasePath
    $versionFile = Get-PhpActiveVersionFile -BasePath $BasePath
    if (Test-Path $versionFile -PathType Leaf) {
        $version = [IO.File]::ReadAllText($versionFile).Trim()
        if ($version -and (Test-Path (Join-Path $BasePath $version) -PathType Container)) {
            return $version
        }
    }
    return ''
}

function Set-PhpActiveVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Version,
        [string] $BasePath = '',
        [string] $SystemPath = 'Prompt'
    )

    $BasePath = Get-PhpBasePath -BasePath $BasePath
    $versionDir = Join-Path $BasePath $Version
    if (-not (Test-Path $versionDir -PathType Container)) {
        throw "PHP version $Version is not installed at $versionDir."
    }

    $versionFile = Get-PhpActiveVersionFile -BasePath $BasePath
    [IO.File]::WriteAllText($versionFile, $Version, [Text.UTF8Encoding]::new($false))

    $phpPath = Join-Path $versionDir 'php.exe'
    if (-not (Test-Path $phpPath -PathType Leaf)) {
        throw "php.exe not found in $versionDir."
    }

    $escapedBasePath = [regex]::Escape($BasePath)
    $versionPattern = "^$escapedBasePath\\[\d\.]+\\?$"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $pathEntries = @($userPath -split ';' | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        $_ -notmatch $versionPattern
    })

    [Environment]::SetEnvironmentVariable('Path', (@($versionDir) + $pathEntries) -join ';', 'User')

    $processPath = @($env:Path -split ';' | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        $_ -notmatch $versionPattern
    })
    $env:Path = (@($versionDir) + $processPath) -join ';'

    $env:PHPRC = Join-Path $versionDir 'php.ini'

    if ($SystemPath -notin @('Prompt', 'Yes', 'No')) {
        throw "Invalid SystemPath value '$SystemPath'. Use Prompt, Yes, or No."
    }
    if ($SystemPath -eq 'Prompt') {
        $answer = (Read-Host 'Also add to system PATH for services? [y/N]').Trim().ToLowerInvariant()
        $SystemPath = if ($answer -in @('y', 'yes')) { 'Yes' } else { 'No' }
    }
    if ($SystemPath -eq 'Yes') {
        $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            Write-Warning 'System PATH requires administrator privileges. Run PowerShell as administrator to update system PATH.'
        } else {
            $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
            $machineEntries = @($machinePath -split ';' | Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $_ -notmatch $versionPattern
            })
            [Environment]::SetEnvironmentVariable('Path', (@($versionDir) + $machineEntries) -join ';', 'Machine')
            Write-Host "Added $versionDir to system PATH." -ForegroundColor Green
        }
    }
}

function Get-PhpInstalledVersions {
    [CmdletBinding()]
    param(
        [string] $BasePath = ''
    )

    $BasePath = Get-PhpBasePath -BasePath $BasePath
    if (-not (Test-Path $BasePath -PathType Container)) {
        return @()
    }

    $activeVersion = Get-PhpActiveVersion -BasePath $BasePath

    Get-ChildItem -Path $BasePath -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
        ForEach-Object {
            $phpPath = Join-Path $_.FullName 'php.exe'
            $installed = Test-Path $phpPath -PathType Leaf
            [PSCustomObject]@{
                Version   = $_.Name
                Path      = $_.FullName
                Installed = $installed
                Active    = ($_.Name -eq $activeVersion)
            }
        } |
        Sort-Object { [Version] $_.Version } -Descending
}

function Get-PhpAvailableVersions {
    [CmdletBinding()]
    param(
        [string] $BasePath = ''
    )

    $BasePath = Get-PhpBasePath -BasePath $BasePath
    $versionCachePath = Join-Path $PSScriptRoot '.php-versions.cache'

    if (Test-Path $versionCachePath -PathType Leaf) {
        $raw = Get-Content $versionCachePath -Raw | ConvertFrom-Json
        $versions = @($raw | ForEach-Object { $_.ToString() })
        return @($versions | Sort-Object { [Version] $_ } -Descending)
    }

    Write-Verbose "No cached PHP versions found. Run Update-PhpVersionCatalog.ps1 first."
    return @()
}

function Test-PhpVersionInstalled {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Version,
        [string] $BasePath = ''
    )

    $BasePath = Get-PhpBasePath -BasePath $BasePath
    $versionDir = Join-Path $BasePath $Version
    $phpPath = Join-Path $versionDir 'php.exe'
    return (Test-Path $phpPath -PathType Leaf)
}
