# Installs the .msix sitting next to this script. Right-click and
# "Run with PowerShell", or: powershell -ExecutionPolicy Bypass -File .\Install.ps1
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$msix = Get-ChildItem -LiteralPath $here -Filter *.msix | Select-Object -First 1
$cer = Get-ChildItem -LiteralPath $here -Filter *.cer | Select-Object -First 1

if ($null -eq $msix) {
    throw "No .msix file was found next to Install.ps1."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$($MyInvocation.MyCommand.Path)`""
    )
    if ($Force) {
        $arguments += "-Force"
    }
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments | Out-Null
    return
}

if ($null -ne $cer) {
    certutil.exe -addstore TrustedPeople $cer.FullName | Out-Null
}

if ($Force) {
    Add-AppxPackage -Path $msix.FullName -ForceUpdateFromAnyVersion -ForceApplicationShutdown
} else {
    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
}

Write-Host "Installed $($msix.Name)."
