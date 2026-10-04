<#
.SYNOPSIS
    Builds FFConvert: the shell extension first, then the app as an MSIX package.

.DESCRIPTION
    Writes artifacts\FFConvert.msix. The package is unsigned; sign.ps1 signs it
    and install.ps1 signs and installs it.

.PARAMETER Configuration
    Build configuration of the app. The shell extension is always Release,
    because Native AOT only applies to a publish.

.PARAMETER Version
    Version of the package, such as 1.2.0. Leave it out for a development
    build, which gets a number below 1.0 that rises with every build.

.PARAMETER Register
    Also unpack the package to artifacts\layout and register it from there for
    the current user. This is the quick loop while developing and needs
    Windows Developer Mode; use install.ps1 for a real install.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [version]$Version,

    [switch]$Register
)

. (Join-Path $PSScriptRoot 'common.ps1')

# Windows refuses to install a package whose version is already installed with
# different content, so a build without -Version gets a number of its own:
# 0.<days since 2026-01-01>.<minute of the day>. The leading 0 keeps these
# development builds below every release.
if ($Version) {
    # Package versions have four parts; "1.2.0" leaves the last one unset.
    $Version = [version]::new($Version.Major, $Version.Minor, [Math]::Max($Version.Build, 0), [Math]::Max($Version.Revision, 0))
} else {
    $now = Get-Date
    $Version = [version]::new(0, ($now.Date - [datetime]'2026-01-01').Days, $now.Hour * 60 + $now.Minute, 0)
}

$stampedManifest = Join-Path $Artifacts 'obj\Package.appxmanifest'
New-Item -ItemType Directory -Force (Split-Path $stampedManifest) | Out-Null
$manifest = New-Object xml
$manifest.PreserveWhitespace = $true
$manifest.Load((Join-Path $RepoRoot 'src\FFConvert.App\Package.appxmanifest'))
$manifest.Package.Identity.Version = $Version.ToString(4)
$manifest.Save($stampedManifest)

# The Native AOT targets run Visual Studio's vcvarsall.bat, which calls a bare
# "vswhere.exe". When that is not on PATH its error text ends up in the linker
# path and the publish fails, so make it resolvable.
$env:PATH = "$env:PATH;$(Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer')"

Write-Host 'Publishing the shell extension (Native AOT)...'
Invoke-Native dotnet @(
    'publish', (Join-Path $RepoRoot 'src\FFConvert.Shell\FFConvert.Shell.csproj'),
    '-c', 'Release', '-o', (Join-Path $Artifacts 'shell'), '--nologo', '-v:q')

Write-Host "Building the app package $Version ($Configuration)..."
$packageDir = Join-Path $Artifacts 'package'
if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }

# The trailing separator is required by the packaging targets. A forward slash
# is used because a backslash before the closing quote would escape it.
Invoke-Native dotnet @(
    'build', (Join-Path $RepoRoot 'src\FFConvert.App\FFConvert.App.csproj'),
    '-c', $Configuration, '-p:Platform=x64', '--nologo', '-v:q',
    '-p:GenerateAppxPackageOnBuild=true',
    '-p:AppxPackageSigningEnabled=false',
    '-p:AppxBundle=Never',
    '-p:UapAppxPackageBuildMode=SideloadOnly',
    '-p:AppxSymbolPackageEnabled=false',
    "-p:StampedPackageManifest=$stampedManifest",
    "-p:AppxPackageDir=$($packageDir.Replace('\', '/'))/")

$built = Get-ChildItem $packageDir -Recurse -Filter '*.msix' | Select-Object -First 1
if (-not $built) { throw 'The build did not produce an .msix package.' }
Copy-Item $built.FullName $PackagePath -Force
Write-Host "Package: $PackagePath"

if ($Register) {
    $layout = Join-Path $Artifacts 'layout'

    # Removing the registration also ends the app and the context menu host,
    # which otherwise keep files in the layout folder locked.
    Get-AppxPackage -Name $PackageName | Remove-AppxPackage -PreserveApplicationData
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }

    Invoke-Native (Find-SdkTool 'makeappx.exe') @('unpack', '/p', $PackagePath, '/d', $layout, '/o') | Out-Null
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    Write-Host "Registered from $layout"
}
