<#
.SYNOPSIS
    Installs FFConvert for the current user.

.DESCRIPTION
    FFConvert is signed with a self-signed certificate, and Windows only
    installs packages signed with a certificate the machine trusts. This
    script adds FFConvert.cer to the machine's Trusted People store, which
    needs administrator rights once, and then installs the package. Later
    versions signed with the same certificate install without elevation.

    Run it from the folder a release was extracted to:

        powershell -ExecutionPolicy Bypass -File Install.ps1

    Settings and presets are kept when a version is replaced.

.PARAMETER Package
    The .msix to install. Defaults to the one next to this script.

.PARAMETER Certificate
    The .cer the package was signed with. Defaults to the one next to this script.
#>
[CmdletBinding()]
param(
    [string]$Package,
    [string]$Certificate
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Beside([string]$Filter) {
    $found = Get-ChildItem $here -Filter $Filter | Select-Object -First 1
    if (-not $found) { throw "No $Filter file was found in $here." }
    return $found.FullName
}

if (-not $Package) { $Package = Find-Beside '*.msix' }
if (-not $Certificate) { $Certificate = Find-Beside '*.cer' }
$Package = (Resolve-Path $Package).Path
$Certificate = (Resolve-Path $Certificate).Path

$thumbprint = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $Certificate).Thumbprint

function Test-Trusted {
    [bool](Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $thumbprint })
}

if (-not (Test-Trusted)) {
    Write-Host 'Asking for administrator rights to trust the signing certificate...'
    # Passed encoded, so the path survives the trip through two command lines.
    $command = "Import-Certificate -FilePath '$($Certificate.Replace("'", "''"))' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    Start-Process powershell.exe -Verb RunAs -Wait -WindowStyle Hidden `
        -ArgumentList '-NoProfile', '-EncodedCommand', $encoded

    if (-not (Test-Trusted)) {
        throw 'The signing certificate was not trusted, so the package cannot be installed. Run the script again and accept the administrator prompt.'
    }
}

# A copy registered from a build folder ("build.ps1 -Register") has the same
# identity and would block the install.
Get-AppxPackage -Name FFConvert | Where-Object IsDevelopmentMode | Remove-AppxPackage -PreserveApplicationData

Write-Host 'Installing...'
# Development builds are numbered below every release, so going from one to
# the other can be a downgrade, which Windows refuses unless told otherwise.
Add-AppxPackage -Path $Package -ForceApplicationShutdown -ForceUpdateFromAnyVersion

$installed = Get-AppxPackage -Name FFConvert
Write-Host "Installed FFConvert $($installed.Version). Right-click a video, audio or image file to use it."
