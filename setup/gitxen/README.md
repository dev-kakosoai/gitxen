# Packaging and distribution for Gitxen

Everything needed to turn a build of `src/app/GitExtensions.WinUI` into something a user can install.
Gitxen ships separately from Git Extensions: its own tags (`gitxen-v1.2.3`), its own workflow
(`.github/workflows/gitxen-release.yml`), its own version (`version.txt`), and its own installer.
Nothing here touches `setup/installer`, which builds the Git Extensions MSI.

## Layout

| Path | What it is |
| --- | --- |
| `version.txt` | The version, and the only place it is written down. |
| `Package.wxs` | The MSI: per-machine, Start menu shortcut, `PATH` entry, GPL licence dialog. |
| `gitxen.cmd` | Shim so that `gitxen` starts the application, in every channel. |
| `install.ps1` | The `irm ... \| iex` installer, and its own uninstaller. |
| `build/Build-Gitxen.ps1` | Builds every artifact and renders every manifest. |
| `packaging/winget/` | winget manifest templates (`Kakoso.Gitxen`). |
| `packaging/chocolatey/` | Chocolatey nuspec and install scripts (`gitxen`). |
| `packaging/scoop/` | Scoop manifest (`gitxen`). |
| `packaging/msix/` | MSIX manifest, for sideloading or a Store submission. |

The files under `packaging/` are **templates**. They carry `{{PLACEHOLDERS}}` and are not valid
manifests as they stand: the build fills in the version, the download URLs, the checksums and the
MSI's ProductCode, and writes the result to `artifacts/Release/gitxen/packaging/`. That is the copy
you submit. Nothing is rendered back into the repository, because a rendered manifest describes one
specific release.

## Building everything locally

```
dotnet tool install --global wix --version 5.0.2
wix extension add --global WixToolset.UI.wixext/5.0.2

pwsh ./setup/gitxen/build/Build-Gitxen.ps1
```

Output lands in `artifacts/Release/gitxen/`:

```
Gitxen-0.1.0-x64.msi     ~96 MB   per-machine installer  (winget, Chocolatey, direct download)
Gitxen-0.1.0-x64.zip    ~122 MB   portable               (Scoop, install.ps1)
Gitxen-0.1.0-x64.msix   ~100 MB   only with -Msix, unsigned
SHA256SUMS.txt
packaging/                        rendered manifests
```

`-SkipPublish` reuses the payload from the previous run, which turns a two-minute rebuild into a
twenty-second one while iterating on the packaging itself. `-Msix` additionally builds the MSIX,
which needs `makeappx.exe` from the Windows SDK.

### Why WiX 5 and not the current release

WiX 6 and 7 refuse to build until the [Open Source Maintenance
Fee](https://wixtoolset.org/osmf/) EULA is accepted, which is a licensing decision about how this
product is shipped rather than something a build script should make on anyone's behalf. WiX 5 is the
last version without that gate, so both the script and the workflow pin it. Moving to 6 or 7 means
accepting those terms first, then lifting the pin in `Build-Gitxen.ps1` and `gitxen-release.yml`.

## The payload

`dotnet publish -r win-x64 --self-contained -p:WindowsAppSDKSelfContained=true`, so the .NET runtime
and the Windows App SDK travel inside the package. That is what makes the download large (~300 MB
before compression) and it is a deliberate trade: a package manager install has to work on a clean
machine, and a missing prerequisite becomes a bug report rather than a prompt.

Two things in `GitExtensions.WinUI.csproj` exist for this and should not be removed without checking
the published application still *launches*:

- `RuntimeFrameworkVersion` is cleared. The repo pins it to `10.0.0` for `Microsoft.NETCore.App`, and
  MSBuild applies one version to every framework reference, so a RID-specific restore asks for a
  `Microsoft.Windows.SDK.NET.Ref 10.0.0` that does not exist.
- `PublishXamlCompilerOutputs` copies the `.xbf` files and the app's `.pri` into the publish folder.
  The SDK's publish pipeline does not track them, and without the `.pri` the application dies at
  startup inside `Microsoft.UI.Xaml.dll` with a stowed exception and no managed stack.

## Releasing

1. Bump `version.txt` and commit it.
2. Tag and push: `git tag gitxen-v0.1.0 && git push origin gitxen-v0.1.0`.
3. `gitxen-release.yml` builds the packages, creates the GitHub release, and attaches the MSI, the
   zip and `SHA256SUMS.txt`. The MSIX and the rendered manifests stay on the workflow run.

Rehearse it first with `Build-Gitxen.ps1` locally, or with the workflow's `workflow_dispatch` trigger,
which builds and uploads artifacts without publishing anything.

### Wiring up the package channels

Each publish job stays inert until its repository variable is set, so a release cannot submit
anything by accident. Set these in the repository's settings once the corresponding account exists:

| Channel | Variable | Secret | What it needs |
| --- | --- | --- | --- |
| winget | `PUBLISH_WINGET=true` | `WINGET_TOKEN` | A classic PAT with `public_repo`, used to fork `microsoft/winget-pkgs` and open a PR. The **first** version has to be submitted by hand (`wingetcreate new`); `wingetcreate update` only works once the package exists. |
| Chocolatey | `PUBLISH_CHOCOLATEY=true` | `CHOCO_API_KEY` | A chocolatey.org account. The first push of a new package ID goes through human moderation, which takes days. |
| Scoop | `PUBLISH_SCOOP=true`, `SCOOP_BUCKET_REPOSITORY` | `SCOOP_BUCKET_TOKEN` | A bucket repository, e.g. `dev-kakosoai/scoop-bucket`, and a PAT that can push to it. |

### Signing

Nothing here is signed. Unsigned, users get a SmartScreen warning on the MSI, and the MSIX cannot be
installed at all. The Git Extensions release pipeline signs through SignPath
(`.github/workflows/app-release.yml`); Gitxen would need its own certificate and project there, or
any other code-signing certificate, applied to the MSI and the MSIX before they are published.

To test the MSIX locally without one, create a self-signed certificate whose subject matches the
manifest's `Publisher`, sign with it, and trust it:

```
New-SelfSignedCertificate -Type Custom -Subject 'CN=Kakoso' -KeyUsage DigitalSignature -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
signtool sign /fd SHA256 /a /n Kakoso Gitxen-0.1.0-x64.msix
```

Then import the certificate into `Local Machine\Trusted People` and `Add-AppxPackage` it. A Store
submission does not need any of this: Partner Center signs the package, but `Identity/Name` and
`Publisher` in `packaging/msix/AppxManifest.xml` must then match the reserved name it assigns.

## How users install

| Channel | Command | Installs |
| --- | --- | --- |
| winget | `winget install Kakoso.Gitxen` | MSI, per-machine |
| Chocolatey | `choco install gitxen` | MSI, per-machine |
| Scoop | `scoop install gitxen` | zip, per-user, no admin |
| PowerShell | `irm https://raw.githubusercontent.com/dev-kakosoai/gitxen/master/setup/gitxen/install.ps1 \| iex` | zip, per-user, no admin |
| Direct | The `.msi` from the release page | MSI, per-machine |

All five leave `gitxen` on `PATH` and a Start menu entry.

`install.ps1` verifies the download against `SHA256SUMS.txt` from the same release before unpacking
anything, and registers itself in Add/Remove Programs so an installation made from a one-liner can
still be removed from the Settings app. It cannot take parameters when run through `iex`, so it also
reads `GITXEN_VERSION`, `GITXEN_INSTALL_DIR`, `GITXEN_NO_PATH`, `GITXEN_NO_SHORTCUT` and
`GITXEN_UNINSTALL` from the environment.

## Channels that are deliberately absent

**Homebrew.** Gitxen is a WinUI 3 application: it runs only on Windows, and a Homebrew cask installs
macOS applications. There is no arrangement under which `brew install gitxen` could produce a working
program. Scoop is the equivalent habit on Windows — a manifest in a bucket, a one-line install, no
administrator rights — and is set up above.

**NuGet.** nuget.org distributes libraries and .NET tools. A GUI application can technically be
packed as a global tool, but it would install a console shim for a windowed application, would not
appear in Add/Remove Programs or the Start menu, and would leave a ~300 MB payload in the tool store.
winget, Chocolatey and Scoop all serve the "install it from a terminal" case properly.

**arm64.** The published payload is x64 only. Windows on Arm runs it under emulation. A native arm64
build means a second publish leg, a second MSI, and a second installer entry in the winget manifest;
the pieces here are all parameterised by architecture, so it is additive work rather than a rewrite.
