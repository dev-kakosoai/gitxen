<#
.SYNOPSIS
    Builds every Gitxen distribution artifact: the MSI, the portable archive, optionally an MSIX,
    and the package manifests that point at them.

.DESCRIPTION
    Runs the same steps locally that .github/workflows/gitxen-release.yml runs for a release, so a
    release can be rehearsed in full before a tag is pushed.

        publish  ->  payload folder (self-contained: .NET and the Windows App SDK are included)
                 ->  Gitxen-<version>-x64.msi      per-machine installer, used by winget and Chocolatey
                 ->  Gitxen-<version>-x64.zip      portable, used by Scoop and the install.ps1 one-liner
                 ->  Gitxen-<version>-x64.msix     optional, for sideloading or a Store submission
                 ->  SHA256SUMS.txt
                 ->  packaging\  manifests with the real version, URLs and checksums filled in

    The manifests are rendered from the templates in ..\packaging. They are written to the output
    folder rather than back into the repository, because they describe one specific release.

.PARAMETER Version
    Three-part version, e.g. 0.1.0. Defaults to the contents of ..\version.txt.

.PARAMETER OutputDir
    Where the artifacts are written. Defaults to artifacts\<configuration>\gitxen.

.PARAMETER SkipPublish
    Reuses the payload from a previous run instead of building it again.

.PARAMETER Msix
    Also builds an MSIX package. Requires makeappx.exe from the Windows SDK.

.PARAMETER MsixPublisher
    Subject name for the MSIX publisher, which has to match the certificate the package will be
    signed with. Defaults to a placeholder that is only good for local testing.

.EXAMPLE
    .\Build-Gitxen.ps1 -Version 0.1.0

.EXAMPLE
    .\Build-Gitxen.ps1 -SkipPublish -Msix
#>
[CmdletBinding()]
param(
    [string] $Version,
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $OutputDir,
    [switch] $SkipPublish,
    [switch] $Msix,
    [string] $MsixPublisher = 'CN=Kakoso'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = 'dev-kakosoai/gitextensions'
$wixToolVersion = '5.0.2'
$setupDir = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $setupDir)
$projectPath = Join-Path $repoRoot 'src\app\GitExtensions.WinUI\GitExtensions.WinUI.csproj'
$iconPath = Join-Path $repoRoot 'src\app\GitExtensions.WinUI\Assets\gitxen.ico'
$logoPath = Join-Path $repoRoot 'src\app\GitExtensions.WinUI\Assets\gitxen-256.png'
$payloadDir = Join-Path $repoRoot "artifacts\$Configuration\publish\Gitxen"

if (-not $Version)
{
    $Version = (Get-Content -Path (Join-Path $setupDir 'version.txt') -Raw).Trim()
}

if ($Version -notmatch '^\d+\.\d+\.\d+$')
{
    throw "Version must be three numeric parts, e.g. 0.1.0. Got '$Version'."
}

if (-not $OutputDir)
{
    $OutputDir = Join-Path $repoRoot "artifacts\$Configuration\gitxen"
}

$tag = "gitxen-v$Version"
$downloadBase = "https://github.com/$repository/releases/download/$tag"
$msiName = "Gitxen-$Version-x64.msi"
$zipName = "Gitxen-$Version-x64.zip"
$msixName = "Gitxen-$Version-x64.msix"

function Write-Stage([string] $message)
{
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Invoke-Checked([string] $description, [scriptblock] $command)
{
    & $command
    if ($LASTEXITCODE -ne 0)
    {
        throw "$description failed with exit code $LASTEXITCODE."
    }
}

function New-Payload
{
    Write-Stage 'Publishing the application'

    if (Test-Path $payloadDir)
    {
        # A publish over the top of an old payload leaves files behind that are no longer part of
        # the application, and they would be packaged along with it.
        Remove-Item -Path $payloadDir -Recurse -Force
    }

    Invoke-Checked 'dotnet publish' {
        dotnet publish $projectPath `
            -c $Configuration `
            -p:Platform=x64 `
            -r win-x64 `
            --self-contained true `
            -p:WindowsAppSDKSelfContained=true `
            -p:Version=$Version `
            --nologo `
            -v:m
    }

    $executable = Join-Path $payloadDir 'GitExtensions.WinUI.exe'
    if (-not (Test-Path $executable))
    {
        throw "Publish produced no executable at $executable."
    }

    $size = (Get-ChildItem -Path $payloadDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("    payload: {0:N0} files, {1:N0} MB" -f (Get-ChildItem -Path $payloadDir -Recurse -File).Count, ($size / 1MB))
}

function Add-PayloadExtras
{
    # Files that belong in the shipped payload but are not build output. Applied on every run, not
    # only after a publish, so that -SkipPublish cannot quietly package an installation missing its
    # licence or its command-line entry point.

    # Shipped in every channel so that `gitxen` means the same thing whichever one installed it.
    Copy-Item -Path (Join-Path $setupDir 'gitxen.cmd') -Destination $payloadDir -Force

    # The licence has to travel with the binaries: this is a GPL-3.0 application.
    Copy-Item -Path (Join-Path $repoRoot 'LICENSE.md') -Destination $payloadDir -Force
    Copy-Item -Path (Join-Path $repoRoot 'NOTICE.md') -Destination $payloadDir -Force
}

function ConvertTo-Rtf([string] $sourcePath, [string] $destinationPath)
{
    # The WiX licence dialog renders RTF, and the licence is kept as Markdown. This is a plain-text
    # conversion, not a Markdown renderer: the point is that the text is readable and complete.
    $text = Get-Content -Path $sourcePath -Raw

    $escaped = $text -replace '\\', '\\' -replace '\{', '\{' -replace '\}', '\}'

    # Anything outside ASCII has to be written as an escape, or the reader shows mojibake.
    $escaped = [Regex]::Replace($escaped, '[^\x00-\x7F]', { param($match) "\u$([int][char]$match.Value)?" })

    $paragraphs = ($escaped -split "\r?\n") -join '\par' + "`r`n"

    $rtf = '{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}' + "`r`n" +
           '\fs18 ' + $paragraphs + '}'

    Set-Content -Path $destinationPath -Value $rtf -Encoding ASCII -NoNewline
}

function New-Msi
{
    Write-Stage "Building $msiName"

    $licenseRtf = Join-Path $OutputDir 'License.rtf'
    ConvertTo-Rtf -sourcePath (Join-Path $repoRoot 'LICENSE.md') -destinationPath $licenseRtf

    $wix = Get-Command 'wix' -ErrorAction SilentlyContinue
    if (-not $wix)
    {
        throw "The WiX CLI was not found. Install it with: dotnet tool install --global wix"
    }

    # WiX 6 and 7 refuse to run until the Open Source Maintenance Fee EULA is accepted, which is a
    # licensing decision for whoever ships the product rather than something a build script should
    # make. Version 5 is the last one that builds without it, so that is what is pinned here and in
    # the release workflow. Accepting the OSMF terms and lifting this pin is a deliberate change.
    $wixVersion = ((wix --version) -split '\+')[0]
    if ($wixVersion -notlike '5.*')
    {
        throw "WiX $wixVersion is installed; this build expects 5.x. Run: dotnet tool uninstall --global wix; dotnet tool install --global wix --version $wixToolVersion"
    }

    # The installer UI lives in an extension that the WiX CLI does not ship with, and its version
    # has to match the CLI's. Adding it is idempotent, so this just makes a fresh machine work.
    if ((wix extension list --global 2>$null) -notmatch 'WixToolset\.UI\.wixext')
    {
        Write-Host "    adding WixToolset.UI.wixext $wixVersion"
        Invoke-Checked 'wix extension add' {
            wix extension add --global "WixToolset.UI.wixext/$wixVersion"
        }
    }

    Invoke-Checked 'wix build' {
        wix build (Join-Path $setupDir 'Package.wxs') `
            -arch x64 `
            -ext WixToolset.UI.wixext `
            -d "Version=$Version" `
            -d "PayloadDir=$payloadDir" `
            -d "IconFile=$iconPath" `
            -d "LicenseRtf=$licenseRtf" `
            -o (Join-Path $OutputDir $msiName)
    }
}

function Get-MsiProductCode([string] $path)
{
    # Every build stamps a fresh ProductCode, and winget wants to know it so it can match an
    # installed copy back to the package. It is only readable from the MSI's own Property table.
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($path, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @("SELECT Value FROM Property WHERE Property = 'ProductCode'"))

    try
    {
        $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)

        if (-not $record)
        {
            throw "No ProductCode in $path."
        }

        return $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
    }
    finally
    {
        # Without this the MSI stays locked for the rest of the session, and the next build cannot
        # overwrite it.
        $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
        [GC]::Collect()
    }
}

function New-PortableArchive
{
    Write-Stage "Building $zipName"

    $archive = Join-Path $OutputDir $zipName
    if (Test-Path $archive)
    {
        Remove-Item -Path $archive -Force
    }

    # Compress-Archive takes minutes over a payload this size; the framework call takes seconds.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payloadDir, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)

    Write-Host ("    {0:N0} MB compressed" -f ((Get-Item $archive).Length / 1MB))
}

function New-MsixPackage
{
    Write-Stage "Building $msixName"

    $makeappx = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter 'makeappx.exe' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -like '*\x64' } |
        Sort-Object -Property FullName -Descending |
        Select-Object -First 1

    if (-not $makeappx)
    {
        throw 'makeappx.exe was not found. Install the Windows SDK, or run without -Msix.'
    }

    # MSIX needs its own staging copy: the manifest and the tile images belong in the package but
    # not in the MSI or the zip.
    $staging = Join-Path $OutputDir 'msix-staging'
    if (Test-Path $staging)
    {
        Remove-Item -Path $staging -Recurse -Force
    }

    Copy-Item -Path $payloadDir -Destination $staging -Recurse

    Add-Type -AssemblyName System.Drawing
    $source = [Drawing.Image]::FromFile($logoPath)
    try
    {
        # The three logo sizes the manifest names. They are not among the icon sizes the
        # application ships, so they are resampled from the largest one.
        foreach ($logo in @(@{ Name = 'Square44x44Logo.png'; Size = 44 },
                            @{ Name = 'Square150x150Logo.png'; Size = 150 },
                            @{ Name = 'StoreLogo.png'; Size = 50 }))
        {
            $bitmap = New-Object Drawing.Bitmap($logo.Size, $logo.Size)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try
            {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source, 0, 0, $logo.Size, $logo.Size)
                $bitmap.Save((Join-Path $staging "Assets\$($logo.Name)"), [Drawing.Imaging.ImageFormat]::Png)
            }
            finally
            {
                $graphics.Dispose()
                $bitmap.Dispose()
            }
        }
    }
    finally
    {
        $source.Dispose()
    }

    $manifest = Get-Content -Path (Join-Path $setupDir 'packaging\msix\AppxManifest.xml') -Raw
    $manifest = $manifest.Replace('{{MSIX_PUBLISHER}}', $MsixPublisher).Replace('{{VERSION4}}', "$Version.0")
    Set-Content -Path (Join-Path $staging 'AppxManifest.xml') -Value $manifest -Encoding UTF8

    $package = Join-Path $OutputDir $msixName
    if (Test-Path $package)
    {
        Remove-Item -Path $package -Force
    }

    Invoke-Checked 'makeappx pack' {
        & $makeappx.FullName pack /d $staging /p $package /o
    }

    Remove-Item -Path $staging -Recurse -Force

    Write-Host '    unsigned: sign it before installing, see setup\gitxen\README.md' -ForegroundColor Yellow
}

function New-Checksums
{
    Write-Stage 'Writing SHA256SUMS.txt'

    # Filtered with Where-Object rather than -Include: -Include is ignored unless the path itself is
    # a wildcard, which would leave the checksum file silently empty.
    $lines = Get-ChildItem -Path $OutputDir -File |
        Where-Object { $_.Extension -in '.msi', '.zip', '.msix' } |
        Sort-Object -Property Name |
        ForEach-Object {
            $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
            Write-Host "    $($_.Name)  $hash"
            # Two spaces then the file name: the format sha256sum -c and Scoop both read.
            "$hash  $($_.Name)"
        }

    Set-Content -Path (Join-Path $OutputDir 'SHA256SUMS.txt') -Value $lines -Encoding ASCII
}

function Expand-Templates([hashtable] $tokens)
{
    Write-Stage 'Rendering the package manifests'

    $source = Join-Path $setupDir 'packaging'
    $destination = Join-Path $OutputDir 'packaging'

    if (Test-Path $destination)
    {
        Remove-Item -Path $destination -Recurse -Force
    }

    foreach ($file in Get-ChildItem -Path $source -Recurse -File)
    {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\')

        # The MSIX manifest is rendered into the package itself, not alongside it.
        if ($relative -like 'msix\*')
        {
            continue
        }

        $target = Join-Path $destination $relative
        New-Item -Path (Split-Path -Parent $target) -ItemType Directory -Force | Out-Null

        $content = Get-Content -Path $file.FullName -Raw
        foreach ($token in $tokens.GetEnumerator())
        {
            if ([string]::IsNullOrWhiteSpace($token.Value))
            {
                # An empty checksum or URL would render a manifest that looks complete and installs
                # nothing, so refuse to write one.
                throw "No value for {{$($token.Key)}} while rendering $relative."
            }

            $content = $content.Replace("{{$($token.Key)}}", $token.Value)
        }

        $remaining = [Regex]::Matches($content, '\{\{(\w+)\}\}') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
        if ($remaining)
        {
            throw "$relative still contains unresolved placeholders: $($remaining -join ', ')"
        }

        Set-Content -Path $target -Value $content -Encoding UTF8 -NoNewline
        Write-Host "    $relative"
    }
}

New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null

if ($SkipPublish)
{
    if (-not (Test-Path (Join-Path $payloadDir 'GitExtensions.WinUI.exe')))
    {
        throw "-SkipPublish was given but there is no payload at $payloadDir."
    }

    Write-Stage "Reusing the payload in $payloadDir"
}
else
{
    New-Payload
}

Add-PayloadExtras

New-Msi
New-PortableArchive

if ($Msix)
{
    New-MsixPackage
}

New-Checksums

$checksums = @{}
Get-Content -Path (Join-Path $OutputDir 'SHA256SUMS.txt') | ForEach-Object {
    $parts = $_ -split '\s+', 2
    $checksums[$parts[1].Trim()] = $parts[0]
}

Expand-Templates @{
    VERSION      = $Version
    RELEASE_TAG  = $tag
    RELEASE_DATE = (Get-Date -Format 'yyyy-MM-dd')
    PRODUCT_CODE = Get-MsiProductCode (Join-Path $OutputDir $msiName)
    MSI_URL      = "$downloadBase/$msiName"
    MSI_SHA256   = $checksums[$msiName]
    ZIP_URL      = "$downloadBase/$zipName"
    ZIP_SHA256   = $checksums[$zipName]
}

Write-Host ''
Write-Host "Gitxen $Version is built in $OutputDir" -ForegroundColor Green
