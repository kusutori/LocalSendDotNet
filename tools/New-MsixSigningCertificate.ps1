[CmdletBinding()]
param(
    [string]$Subject = "CN=kusut",
    [string]$CerPath = (Join-Path $PSScriptRoot "..\artifacts\signing\LocalSendDotNet.cer"),
    [int]$YearsValid = 2
)

$ErrorActionPreference = "Stop"

if ($YearsValid -lt 1)
{
    throw "YearsValid must be at least 1."
}

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Subject `
    -FriendlyName "LocalSendDotNet MSIX test signing" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($YearsValid) `
    -TextExtension @(
        "2.5.29.19={critical}{text}ca=false",
        "2.5.29.37={text}1.3.6.1.5.5.7.3.3"
    )

$resolvedCerPath = [System.IO.Path]::GetFullPath($CerPath)
$cerDirectory = [System.IO.Path]::GetDirectoryName($resolvedCerPath)
[System.IO.Directory]::CreateDirectory($cerDirectory) | Out-Null
Export-Certificate -Cert $certificate -FilePath $resolvedCerPath -Force | Out-Null

[pscustomobject]@{
    Subject = $certificate.Subject
    Thumbprint = $certificate.Thumbprint
    CerPath = $resolvedCerPath
    StorePath = $certificate.PSPath
}
