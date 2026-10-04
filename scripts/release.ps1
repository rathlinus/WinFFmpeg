<#
.SYNOPSIS
    Builds everything a GitHub release consists of.

.DESCRIPTION
    Builds and signs the package with the given version and collects the
    files to publish in artifacts\release:

        WinFFmpeg-<version>-x64.zip    the package, the certificate and Install.ps1
        WinFFmpeg-<version>-x64.msix   the package on its own
        WinFFmpeg.cer                  the certificate on its own

    The release workflow runs this for a version tag. It works the same on a
    developer machine, which is how to try a release before tagging one.

.PARAMETER Version
    The release version, such as 1.2.0.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [version]$Version
)

. (Join-Path $PSScriptRoot 'common.ps1')

& (Join-Path $PSScriptRoot 'build.ps1') -Version $Version
& (Join-Path $PSScriptRoot 'sign.ps1')

$release = Join-Path $Artifacts 'release'
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
$bundle = Join-Path $release 'bundle'
New-Item -ItemType Directory -Force $bundle | Out-Null

$name = "WinFFmpeg-$($Version.Major).$($Version.Minor).$([Math]::Max($Version.Build, 0))-x64"
Copy-Item $SignedPackagePath (Join-Path $bundle "$name.msix")
Copy-Item $CertificatePath (Join-Path $bundle 'WinFFmpeg.cer')
Copy-Item (Join-Path $RepoRoot 'packaging\Install.ps1') $bundle

Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath (Join-Path $release "$name.zip")
Move-Item (Join-Path $bundle "$name.msix") $release
Move-Item (Join-Path $bundle 'WinFFmpeg.cer') $release
Remove-Item $bundle -Recurse -Force

Write-Host "Release files:"
Get-ChildItem $release | ForEach-Object { Write-Host "  $($_.FullName)" }
