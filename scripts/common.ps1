# Shared by the other scripts in this folder. Dot-source it; do not run it.

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $RepoRoot 'artifacts'
$PackageName = 'FFConvert'

# What build.ps1 writes, and what sign.ps1 makes of it.
$PackagePath = Join-Path $Artifacts 'FFConvert.msix'
$SignedPackagePath = Join-Path $Artifacts 'FFConvert-signed.msix'
$CertificatePath = Join-Path $Artifacts 'FFConvert.cer'

# Finds a Windows SDK tool such as makeappx.exe or signtool.exe: first in the
# NuGet package the app project restores, then in an installed Windows SDK.
function Find-SdkTool([string]$Name) {
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $candidates = @(
        (Join-Path $nuget "microsoft.windows.sdk.buildtools\*\bin\*\x64\$Name"),
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\*\x64\$Name")
    )

    foreach ($pattern in $candidates) {
        $tool = Get-ChildItem $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }

    throw "$Name was not found. Build the app once (scripts\build.ps1) or install the Windows SDK."
}

# Runs a native command and fails the script when it fails.
function Invoke-Native([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Command)) $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}
