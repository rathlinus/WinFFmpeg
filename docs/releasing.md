# Releasing

## Publish a version

Tag the commit and push the tag:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

The tag has to have the form `v<major>.<minor>.<patch>`. Pushing it starts the Release workflow, which runs the tests, builds the package with that version, signs it and creates a GitHub release with three files:

| File | Contents |
|---|---|
| `WinFFmpeg-1.2.0-x64.zip` | the package, the certificate and `Install.ps1`. This is the download the README points to. |
| `WinFFmpeg-1.2.0-x64.msix` | the package on its own |
| `WinFFmpeg.cer` | the public half of the signing certificate |

GitHub generates the release notes from the changes since the previous tag.

## The signing certificate

Windows installs a package only when it trusts the certificate that signed it. WinFFmpeg uses a self-signed certificate, so each user trusts it once through `Install.ps1`. That only stays a one-time step if every release is signed with the same certificate.

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

This writes the same three files to `artifacts\release`. Extract the zip somewhere and run its `Install.ps1` to install exactly what a user would get.

## Continuous integration

The CI workflow runs on every push to `main` and on pull requests. It installs ffmpeg, runs the tests and builds the package. The unsigned package is attached to the run as an artifact, which is enough to confirm that packaging still works. It cannot be installed without signing.
