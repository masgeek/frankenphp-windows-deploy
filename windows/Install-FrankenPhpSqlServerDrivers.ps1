param(
    [Alias('help', '--help')]
    [switch] $ShowHelp,
    [string] $InstallPath = 'C:\FrankenPHP',
    [string] $DriverVersion = '5.13.3'
)

$ErrorActionPreference = 'Stop'
if ($ShowHelp -or $args -contains '--help' -or $MyInvocation.UnboundArguments -contains '--help' -or $MyInvocation.Line -match '(?:^|\s)--help(?:\s|$)') {
    Write-Host "Usage: $([IO.Path]::GetFileName($PSCommandPath)) [parameters]"
    Get-Help -Name $PSCommandPath -Full | Out-Host
    return
}
[Net.ServicePointManager]::SecurityProtocol = `
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$php = Join-Path $InstallPath 'php.exe'
$extensionPath = Join-Path $InstallPath 'ext'

if (-not (Test-Path $php -PathType Leaf)) {
    throw "The FrankenPHP PHP executable was not found at $php"
}

if (-not (Test-Path $extensionPath -PathType Container)) {
    throw "The FrankenPHP extension directory was not found at $extensionPath"
}

$versionOutput = & $php --version
$phpInfo = & $php -i
if ($LASTEXITCODE -ne 0 -or $versionOutput[0] -notmatch '^PHP (\d+)\.(\d+)\.') {
    throw 'Unable to determine the FrankenPHP PHP build.'
}

$phpVersion = "$($Matches[1])$($Matches[2])"
$threadSafety = if ($phpInfo -match 'Thread Safety => enabled') { 'ts' } else { 'nts' }
$architecture = if ($phpInfo -match 'Architecture => x64') { 'x64' } else { 'x86' }

$archive = Join-Path $env:TEMP "msphpsql-$DriverVersion-$PID.zip"
$extractPath = Join-Path $env:TEMP "msphpsql-$DriverVersion-$PID"
$downloadUrl = "https://github.com/microsoft/msphpsql/releases/download/v$DriverVersion/Windows_$($DriverVersion)RTW.zip"

try {
    Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $archive
    Expand-Archive -Path $archive -DestinationPath $extractPath -Force

    $driverDirectory = Join-Path $extractPath 'Windows'
    $pdoDriver = Join-Path $driverDirectory "php_pdo_sqlsrv_$($phpVersion)_$($threadSafety)_$architecture.dll"
    $sqlDriver = Join-Path $driverDirectory "php_sqlsrv_$($phpVersion)_$($threadSafety)_$architecture.dll"

    if (-not (Test-Path $pdoDriver -PathType Leaf) -or -not (Test-Path $sqlDriver -PathType Leaf)) {
        throw "SQL Server drivers are unavailable for PHP $phpVersion $threadSafety $architecture in release $DriverVersion."
    }

    Copy-Item $pdoDriver (Join-Path $extensionPath 'php_pdo_sqlsrv.dll') -Force
    Copy-Item $sqlDriver (Join-Path $extensionPath 'php_sqlsrv.dll') -Force
} finally {
    Remove-Item $archive -Force -ErrorAction SilentlyContinue
    Remove-Item $extractPath -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Installed Microsoft SQL Server drivers $DriverVersion for PHP $phpVersion $threadSafety $architecture." -ForegroundColor Green
