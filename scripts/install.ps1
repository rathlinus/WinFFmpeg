<#
.SYNOPSIS
    Signs the package and installs it for the current user.

.DESCRIPTION
    Runs sign.ps1 and then packaging\Install.ps1, the same installer a release
    ships with. The first run asks for administrator rights once, to make
    Windows trust the signing certificate.

.PARAMETER Build
    Run build.ps1 first. Implied when no package has been built yet.
#>
[CmdletBinding()]
param(
    [switch]$Build
)

. (Join-Path $PSScriptRoot 'common.ps1')

if ($Build -or -not (Test-Path $PackagePath)) {
    & (Join-Path $PSScriptRoot 'build.ps1')
}

& (Join-Path $PSScriptRoot 'sign.ps1')
& (Join-Path $RepoRoot 'packaging\Install.ps1') -Package $SignedPackagePath -Certificate $CertificatePath
