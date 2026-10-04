# Shared by the other scripts in this folder. Dot-source it; do not run it.

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $RepoRoot 'artifacts'
$PackageName = 'WinFFmpeg'

# What build.ps1 writes, and what sign.ps1 makes of it.
$PackagePath = Join-Path $Artifacts 'WinFFmpeg.msix'
$SignedPackagePath = Join-Path $Artifacts 'WinFFmpeg-signed.msix'
$CertificatePath = Join-Path $Artifacts 'WinFFmpeg.cer'

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

# Finds ISCC.exe, the Inno Setup compiler, wherever its installer put it.
function Find-InnoSetup {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup *\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup *\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup *\ISCC.exe')
    )

    foreach ($pattern in $candidates) {
        $tool = Get-ChildItem $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }

    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw 'Inno Setup was not found. Install it with "winget install JRSoftware.InnoSetup" or run scripts\setup-dev.ps1.'
}

# Runs a native command and fails the script when it fails.
function Invoke-Native([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Command)) $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

# The code-signing certificate from the current user's store. When there is none,
# a self-signed one is created and kept there for later runs.
function Get-SigningCertificate {
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

    return $certificate
}

# Signs a package or an executable in place.
function Add-Signature([string]$Path) {
    $certificate = Get-SigningCertificate
    Invoke-Native (Find-SdkTool 'signtool.exe') @('sign', '/q', '/fd', 'SHA256', '/sha1', $certificate.Thumbprint, $Path)
}
