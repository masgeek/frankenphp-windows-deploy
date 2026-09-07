function Assert-FrankenPhpAdministrator {
    param()

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        return
    }

    throw 'Administrator privileges are required. Re-open PowerShell as Administrator and run this script again.'
}

function Show-FrankenPhpError {
    param([System.Management.Automation.ErrorRecord] $ErrorRecord)

    Write-Host "`nERROR: $($ErrorRecord.Exception.Message)" -ForegroundColor Red
    if ($Host.Name -notmatch 'ServerRemoteHost') {
        [void](Read-Host 'Press Enter to close')
    }
}
