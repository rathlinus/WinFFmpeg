<#
.SYNOPSIS
    One-time machine setup for building WinFFmpeg. Must run elevated.

.DESCRIPTION
    - Installs the .NET 10 SDK (winget).
    - Enables Windows Developer Mode, so the package can be registered
      unsigned from a loose folder during development.
    - Adds the MSVC x64 toolset to Visual Studio Build Tools 2022, which
      the Native AOT linker needs for the shell extension.

    Each step is skipped when it is already satisfied.
#>
[CmdletBinding()]
param(
    [string]$LogPath = (Join-Path $env:TEMP 'winffmpeg-setup.log')
)

$ErrorActionPreference = 'Stop'

function Write-Log([string]$Message) {
    $line = '{0:HH:mm:ss} {1}' -f (Get-Date), $Message
    Write-Host $line
    Add-Content -Path $LogPath -Value $line -Encoding utf8
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Log 'ERROR not elevated'
    exit 1
}

Set-Content -Path $LogPath -Value '' -Encoding utf8
Write-Log 'START'
$failed = $false

# --- .NET 10 SDK ---------------------------------------------------------
try {
    $sdks = & dotnet --list-sdks 2>$null
    if ($sdks -match '^10\.') {
        Write-Log 'dotnet10: already installed'
    } else {
        Write-Log 'dotnet10: installing'
        & winget install --id Microsoft.DotNet.SDK.10 --exact --silent `
            --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-Null
        Write-Log "dotnet10: winget exit code $LASTEXITCODE"
        if ($LASTEXITCODE -ne 0) { $failed = $true }
    }
} catch {
    Write-Log "dotnet10: FAILED $($_.Exception.Message)"
    $failed = $true
}

# --- Developer Mode ------------------------------------------------------
try {
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    Set-ItemProperty -Path $key -Name AllowDevelopmentWithoutDevLicense -Value 1 -Type DWord
    Write-Log 'devmode: enabled'
} catch {
    Write-Log "devmode: FAILED $($_.Exception.Message)"
    $failed = $true
}

# --- MSVC toolset --------------------------------------------------------
try {
    $installerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    $vswhere = Join-Path $installerDir 'vswhere.exe'
    $vcComponent = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'

    $withVc = $null
    if (Test-Path $vswhere) {
        $withVc = & $vswhere -latest -products * -requires $vcComponent -property installationPath
    }

    if ($withVc) {
        Write-Log "msvc: already installed at $withVc"
    } else {
        $buildTools = $null
        if (Test-Path $vswhere) {
            $buildTools = & $vswhere -latest -products Microsoft.VisualStudio.Product.BuildTools `
                -version '[17.0,)' -property installationPath
        }

        if ($buildTools) {
            Write-Log "msvc: adding toolset to $buildTools"
            $setupArgs = @('modify', '--installPath', "`"$buildTools`"", '--add', $vcComponent)
            $sdkLib = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Lib\10.0.26100.0\um\x64\kernel32.lib'
            if (-not (Test-Path $sdkLib)) {
                $setupArgs += @('--add', 'Microsoft.VisualStudio.Component.Windows11SDK.26100')
            }
            $setupArgs += @('--quiet', '--norestart')
            $p = Start-Process -FilePath (Join-Path $installerDir 'setup.exe') -ArgumentList $setupArgs -Wait -PassThru
            Write-Log "msvc: installer exit code $($p.ExitCode)"
            if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { $failed = $true }
        } else {
            Write-Log 'msvc: installing Build Tools 2022 with the C++ workload'
            & winget install --id Microsoft.VisualStudio.2022.BuildTools --exact --silent `
                --accept-package-agreements --accept-source-agreements --disable-interactivity `
                --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended" | Out-Null
            Write-Log "msvc: winget exit code $LASTEXITCODE"
            if ($LASTEXITCODE -ne 0) { $failed = $true }
        }
    }
} catch {
    Write-Log "msvc: FAILED $($_.Exception.Message)"
    $failed = $true
}

if ($failed) {
    Write-Log 'DONE with errors'
    exit 1
}

Write-Log 'DONE ok'
exit 0
