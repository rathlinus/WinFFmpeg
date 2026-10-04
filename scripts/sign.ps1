<#
.SYNOPSIS
    Signs the package build.ps1 produced.

.DESCRIPTION
    Writes artifacts\WinFFmpeg-signed.msix and, next to it, WinFFmpeg.cer: the
    public half of the signing certificate, which a machine has to trust
    before it will install the package.

    The certificate is the code-signing certificate with the subject
    CN=WinFFmpeg in the current user's store. When there is none, a
    self-signed one is created and kept there for later runs.
#>
[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path $PackagePath)) {
    throw "There is no package to sign. Run scripts\build.ps1 first."
}

$certificate = Get-SigningCertificate
Write-Host "Signing with certificate $($certificate.Thumbprint)..."
Copy-Item $PackagePath $SignedPackagePath -Force
Add-Signature $SignedPackagePath
Export-Certificate -Cert $certificate -FilePath $CertificatePath | Out-Null

Write-Host "Signed package: $SignedPackagePath"
