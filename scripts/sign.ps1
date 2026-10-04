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

# Has to equal the Publisher in Package.appxmanifest.
$subject = 'CN=WinFFmpeg'

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    Write-Host 'Creating a signing certificate...'
    # 2.5.29.37 marks it as code signing only; 2.5.29.19 as not being a certificate authority.
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $subject `
        -KeyUsage DigitalSignature -FriendlyName 'WinFFmpeg package signing' `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}

Write-Host "Signing with certificate $($certificate.Thumbprint)..."
Copy-Item $PackagePath $SignedPackagePath -Force
Invoke-Native (Find-SdkTool 'signtool.exe') @('sign', '/q', '/fd', 'SHA256', '/sha1', $certificate.Thumbprint, $SignedPackagePath)
Export-Certificate -Cert $certificate -FilePath $CertificatePath | Out-Null

Write-Host "Signed package: $SignedPackagePath"
