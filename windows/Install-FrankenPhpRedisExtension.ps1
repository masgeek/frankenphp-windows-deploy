param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $InstallPath = 'C:\FrankenPHP',
    [string] $ExtensionVersion = '6.3.0'
)

$ErrorActionPreference = 'Stop'
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

$php = Join-Path $InstallPath 'php.exe'
$phpIni = Join-Path $InstallPath 'php.ini'
$extensionPath = Join-Path $InstallPath 'ext'

if (-not (Test-Path $php -PathType Leaf)) {
    throw "The FrankenPHP PHP executable was not found at $php"
}

if (-not (Test-Path $extensionPath -PathType Container)) {
    throw "The FrankenPHP extension directory was not found at $extensionPath"
}

if (-not (Test-Path $phpIni -PathType Leaf)) {
    throw "The FrankenPHP configuration was not found at $phpIni"
}

$versionOutput = & $php --version
$phpInfo = & $php -i
if ($LASTEXITCODE -ne 0 -or $versionOutput[0] -notmatch '^PHP (\d+\.\d+)\.') {
    throw 'Unable to determine the FrankenPHP PHP build.'
}

$phpVersion = $Matches[1]
$phpInfoText = $phpInfo -join [Environment]::NewLine
$threadSafety = if ($phpInfoText -match 'Thread Safety => enabled') { 'ts' } else { 'nts' }
$architecture = if ($phpInfoText -match 'Architecture => x64') { 'x64' } else { 'x86' }

if ($phpInfoText -match 'PHP Extension Build => .*?,(VS\d+)') {
    $compiler = $Matches[1].ToLowerInvariant()
} else {
    $compiler = switch ([Version] $phpVersion) {
        { $_ -ge [Version] '8.4' } { 'vs17'; break }
        { $_ -ge [Version] '8.0' } { 'vs16'; break }
        default { 'vc15' }
    }
}
$package = "php_redis-$ExtensionVersion-$phpVersion-$threadSafety-$compiler-$architecture"
$archive = Join-Path $env:TEMP "$package-$PID.zip"
$extractPath = Join-Path $env:TEMP "$package-$PID"
$downloadUrl = "https://windows.php.net/downloads/pecl/releases/redis/$ExtensionVersion/$package.zip"

try {
    Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $archive
    Expand-Archive -Path $archive -DestinationPath $extractPath -Force

    $redisExtension = Join-Path $extractPath 'php_redis.dll'
    if (-not (Test-Path $redisExtension -PathType Leaf)) {
        throw "The Redis extension package does not contain php_redis.dll: $downloadUrl"
    }

    Copy-Item $redisExtension (Join-Path $extensionPath 'php_redis.dll') -Force

    Get-ChildItem $extractPath -Filter '*.dll' -File -Recurse |
        Where-Object { $_.Name -ne 'php_redis.dll' } |
        ForEach-Object {
            Copy-Item $_.FullName (Join-Path $InstallPath $_.Name) -Force
        }
} finally {
    Remove-Item $archive -Force -ErrorAction SilentlyContinue
    Remove-Item $extractPath -Recurse -Force -ErrorAction SilentlyContinue
}

$iniContent = [IO.File]::ReadAllText($phpIni)
$redisPattern = '(?m)^\s*;?\s*extension\s*=\s*(?:php_)?redis(?:\.dll)?\s*$'
if ([Regex]::IsMatch($iniContent, $redisPattern)) {
    $iniContent = [Regex]::Replace($iniContent, $redisPattern, 'extension=redis')
} else {
    $nextSection = [Regex]::Match($iniContent, '(?m)^\[(?!PHP\])')
    $redisDirective = 'extension=redis' + [Environment]::NewLine + [Environment]::NewLine
    if ($nextSection.Success) {
        $iniContent = $iniContent.Insert($nextSection.Index, $redisDirective)
    } else {
        $iniContent = $iniContent.TrimEnd() + [Environment]::NewLine + $redisDirective
    }
}

[IO.File]::WriteAllText($phpIni, $iniContent, [Text.UTF8Encoding]::new($false))

$env:PHPRC = $phpIni
$env:FRANKENPHP_EXT_DIR = $extensionPath
$modules = & $php -m
if ($LASTEXITCODE -ne 0 -or 'redis' -notin $modules) {
    throw 'The Redis extension was installed but PHP could not load it.'
}

Write-Host "Installed and enabled Redis extension $ExtensionVersion for PHP $phpVersion $threadSafety $compiler $architecture." -ForegroundColor Green
