# Releasing

## Publish a version

Tag the commit and push the tag:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

The tag has to have the form `v<major>.<minor>.<patch>`. Pushing it starts the Release workflow, which runs the tests, builds the package with that version, signs it and creates a GitHub release with four files:

| File | Contents |
|---|---|
| `WinFFmpeg-1.2.0-Setup.exe` | the setup program. This is the download the README points to. |
| `WinFFmpeg-1.2.0-x64.zip` | the package, the certificate and `Install.ps1`, for installing without the setup program |
| `WinFFmpeg-1.2.0-x64.msix` | the package on its own |
| `WinFFmpeg.cer` | the public half of the signing certificate |

GitHub generates the release notes from the changes since the previous tag.

## The signing certificate

Windows installs a package only when it trusts the certificate that signed it. WinFFmpeg uses a self-signed certificate, so each user trusts it once during installation. That only stays a one-time step if every release is signed with the same certificate.

The workflow reads the certificate from two repository secrets:

| Secret | Value |
|---|---|
| `SIGNING_CERTIFICATE_PFX` | the certificate with its private key, as a base64-encoded PFX file |
| `SIGNING_CERTIFICATE_PASSWORD` | the password of that PFX file |

Without these secrets the workflow still produces a release, signed with a certificate it creates on the spot. Users then have to accept the administrator prompt again for every version, because each one comes with a different certificate.

To store the certificate `scripts\sign.ps1` created on your machine:

```powershell
$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=WinFFmpeg'
$password = Read-Host -AsSecureString 'Password for the PFX file'
Export-PfxCertificate -Cert $certificate -FilePath WinFFmpeg.pfx -Password $password

[Convert]::ToBase64String([IO.File]::ReadAllBytes('WinFFmpeg.pfx')) | gh secret set SIGNING_CERTIFICATE_PFX
gh secret set SIGNING_CERTIFICATE_PASSWORD    # asks for the password

Remove-Item WinFFmpeg.pfx
```

Treat the PFX file like a password. Whoever has it can sign packages that your users' machines will install without asking.

The certificate is valid for five years. `sign.ps1` creates a replacement when the current one has less than 30 days left, and the secrets have to be updated with it. Users trust the replacement once, as they did the first.

## Try a release before tagging

```powershell
scripts\release.ps1 -Version 1.2.0
```

This writes the same four files to `artifacts\release`, so you can run the setup program exactly as a user would get it. It needs Inno Setup, which `scripts\setup-dev.ps1` installs.

Windows will not install a version over the same version with different content. A release you tried locally as 1.2.0 therefore blocks the published 1.2.0 on your machine until you uninstall it. Trying it under a lower number, such as 1.1.9, avoids that.

## Continuous integration

The CI workflow runs on every push to `main` and on pull requests. It installs ffmpeg and Inno Setup, runs the tests and then runs `release.ps1` with a placeholder version, so a problem in the release build shows up before a tag is pushed. Those files are signed with a certificate made for the run and are not kept. Only the unsigned package is attached to the run as an artifact; it cannot be installed without signing.

## The setup program

`packaging\WinFFmpeg.iss` is an Inno Setup script. The setup program it produces carries the signed package, the certificate and `Install.ps1`, unpacks them to a temporary folder and runs the script. It installs no files of its own and registers no uninstaller, so WinFFmpeg appears once under Settings, Apps and is removed there.

Setup runs without administrator rights. `Install.ps1` asks for them only when the certificate is not trusted yet, so an update needs no prompt. `Setup.exe /VERYSILENT` installs without showing a window.

The setup program is signed with the same certificate as the package. Because that certificate is self-signed, Windows SmartScreen can still warn about the download. A certificate from a public certificate authority is the only thing that removes that warning.
