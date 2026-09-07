[CmdletBinding()]
param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $CachePath = (Join-Path $PSScriptRoot '.php-versions.cache')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [-CachePath <path>]"
    return
}

trap {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($Host.Name -notmatch 'ServerRemoteHost') { [void](Read-Host 'Press Enter to close') }
    exit 1
}

$indexUrls = @(
    'https://windows.php.net/downloads/releases/'
    'https://downloads.php.net/~windows/releases/'
    'https://downloads.php.net/~windows/releases/archives/'
)
$versions = @()
foreach ($indexUrl in $indexUrls) {
    try {
        Write-Verbose "Reading PHP version catalog '$indexUrl'."
        $content = (Invoke-WebRequest -UseBasicParsing -Uri $indexUrl).Content
        $versions += [Regex]::Matches(
            $content,
            'php-(\d+\.\d+\.\d+)-nts-Win32-vs17-x64\.zip',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase
        ) | ForEach-Object { $_.Groups[1].Value }
    } catch {
        Write-Verbose "Unable to read '$indexUrl': $($_.Exception.Message)"
    }
}

$versions = @($versions | Sort-Object { [Version] $_ } -Descending -Unique)
if ($versions.Count -eq 0) {
    throw 'No compatible PHP versions were found in the configured release catalogs.'
}

$CachePath = [IO.Path]::GetFullPath($CachePath)
New-Item -ItemType Directory -Path (Split-Path -Parent $CachePath) -Force | Out-Null
[IO.File]::WriteAllText($CachePath, ($versions | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Host "Cached $($versions.Count) PHP versions in $CachePath." -ForegroundColor Green
