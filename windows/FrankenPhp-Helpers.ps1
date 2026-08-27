function Assert-FrankenPhpAdministrator {
    param(
        [hashtable] $BoundParameters,
        [string[]] $UnboundArguments,
        [string] $ScriptPath
    )

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        return
    }

    $argumentList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath)
    foreach ($parameter in $BoundParameters.GetEnumerator()) {
        if ($parameter.Value -is [switch] -and $parameter.Value.IsPresent) {
            $argumentList += "-$($parameter.Key)"
        } elseif ($null -ne $parameter.Value) {
            $argumentList += "-$($parameter.Key)"
            $argumentList += [string] $parameter.Value
        }
    }
    $argumentList += $UnboundArguments

    $hostExecutable = if ($PSVersionTable.PSEdition -eq 'Core') {
        Join-Path $PSHOME 'pwsh.exe'
    } else {
        Join-Path $PSHOME 'powershell.exe'
    }

    Write-Host 'Administrator privileges are required. Requesting elevation...' -ForegroundColor Yellow
    $elevatedProcess = Start-Process `
        -FilePath $hostExecutable `
        -Verb RunAs `
        -ArgumentList $argumentList `
        -Wait `
        -PassThru
    exit $elevatedProcess.ExitCode
}
