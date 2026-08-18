<#
.SYNOPSIS
    Installs Gitxen for the current user, without administrator rights.

.DESCRIPTION
    Downloads the portable archive from the project's GitHub releases, verifies it against the
    published SHA256 checksum, and unpacks it into %LOCALAPPDATA%\Programs\Gitxen. Adds a Start menu
    shortcut, puts `gitxen` on PATH, and registers an entry in Add/Remove Programs that points back
    at this script.

    Designed to be run straight from the web:

        irm https://raw.githubusercontent.com/dev-kakosoai/gitextensions/master/setup/gitxen/install.ps1 | iex

    A script run that way cannot be given parameters, so each one is also read from an environment
    variable:

        $env:GITXEN_VERSION     = '0.2.0'          # a specific release instead of the newest
        $env:GITXEN_INSTALL_DIR = 'D:\Tools\Gitxen'
        $env:GITXEN_NO_PATH     = '1'
        $env:GITXEN_UNINSTALL   = '1'

.PARAMETER Version
    Release to install, e.g. 0.1.0. Defaults to the newest Gitxen release.

.PARAMETER InstallDir
    Where to install. Defaults to %LOCALAPPDATA%\Programs\Gitxen.

.PARAMETER NoPathUpdate
    Leaves PATH alone.

.PARAMETER NoShortcut
    Skips the Start menu shortcut.

.PARAMETER Uninstall
    Removes an existing installation instead of installing.
#>
[CmdletBinding()]
param(
    [string] $Version = $env:GITXEN_VERSION,
    [string] $InstallDir = $env:GITXEN_INSTALL_DIR,
    [switch] $NoPathUpdate = [bool]$env:GITXEN_NO_PATH,
    [switch] $NoShortcut = [bool]$env:GITXEN_NO_SHORTCUT,
    [switch] $Uninstall = [bool]$env:GITXEN_UNINSTALL
)

$ErrorActionPreference = 'Stop'

$repository = 'dev-kakosoai/gitextensions'
$tagPrefix = 'gitxen-v'
$executableName = 'GitExtensions.WinUI.exe'
$displayName = 'Gitxen'
$registryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Gitxen'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Gitxen.lnk'

if (-not $InstallDir)
{
    $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Gitxen'
}

# Windows PowerShell 5.1 still negotiates TLS 1.0 by default, which github.com refuses.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Write-Step([string] $message)
{
    Write-Host "  $message" -ForegroundColor Cyan
}

function Stop-RunningGitxen([string] $directory)
{
    $processName = [IO.Path]::GetFileNameWithoutExtension($executableName)
    $running = @(Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($directory, [StringComparison]::OrdinalIgnoreCase) })

    if ($running.Count -eq 0)
    {
        return
    }

    Write-Step "Closing $($running.Count) running instance(s)"
    $running | Stop-Process -Force
    # Give Windows a moment to release the file handles before the directory is replaced.
    Start-Sleep -Milliseconds 500
}

function Get-UserPathEntries
{
    $path = [Environment]::GetEnvironmentVariable('PATH', 'User')
    if (-not $path)
    {
        return @()
    }

    return @($path.Split(';') | Where-Object { $_ })
}

function Remove-Installation
{
    Write-Host "Uninstalling $displayName" -ForegroundColor White

    $installed = (Get-ItemProperty -Path $registryKey -Name 'InstallLocation' -ErrorAction SilentlyContinue).InstallLocation
    $target = if ($installed) { $installed } else { $InstallDir }

    if (-not (Test-Path $target))
    {
        Write-Warning "Nothing installed at $target."
    }
    else
    {
        Stop-RunningGitxen $target
        Write-Step "Removing $target"
        Remove-Item -Path $target -Recurse -Force
    }

    if (Test-Path $shortcutPath)
    {
        Write-Step 'Removing the Start menu shortcut'
        Remove-Item -Path $shortcutPath -Force
    }

    $entries = Get-UserPathEntries
    $remaining = @($entries | Where-Object { $_.TrimEnd('\') -ne $target.TrimEnd('\') })
    if ($remaining.Count -ne $entries.Count)
    {
        Write-Step 'Removing the PATH entry'
        [Environment]::SetEnvironmentVariable('PATH', ($remaining -join ';'), 'User')
    }

    if (Test-Path $registryKey)
    {
        Remove-Item -Path $registryKey -Recurse -Force
    }

    Write-Host "$displayName has been removed." -ForegroundColor Green
}

function Get-LatestVersion
{
    Write-Step 'Looking up the newest release'

    # /releases/latest is no use here: this repository also publishes Git Extensions releases, and
    # the newest of those would win. Filter the list down to Gitxen's own tags instead.
    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases?per_page=100" -Headers @{ 'User-Agent' = 'gitxen-installer' }
    $release = $releases |
        Where-Object { $_.tag_name -like "$tagPrefix*" -and -not $_.draft -and -not $_.prerelease } |
        Select-Object -First 1

    if (-not $release)
    {
        throw "No published $displayName release found in $repository."
    }

    return $release.tag_name.Substring($tagPrefix.Length)
}

function Install-Gitxen
{
    if (-not $Version)
    {
        $Version = Get-LatestVersion
    }

    if (-not [Environment]::Is64BitOperatingSystem)
    {
        throw "$displayName requires 64-bit Windows."
    }

    $tag = "$tagPrefix$Version"
    $archiveName = "Gitxen-$Version-x64.zip"
    $baseUrl = "https://github.com/$repository/releases/download/$tag"

    Write-Host "Installing $displayName $Version" -ForegroundColor White

    $staging = Join-Path ([IO.Path]::GetTempPath()) "gitxen-$([Guid]::NewGuid().ToString('n'))"
    New-Item -Path $staging -ItemType Directory -Force | Out-Null

    try
    {
        $archive = Join-Path $staging $archiveName

        Write-Step "Downloading $archiveName"
        # Invoke-WebRequest's progress bar makes a large download several times slower under Windows
        # PowerShell, because it repaints the console for every chunk that arrives.
        $previousProgress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        try
        {
            Invoke-WebRequest -Uri "$baseUrl/$archiveName" -OutFile $archive -UseBasicParsing
            $checksums = (Invoke-WebRequest -Uri "$baseUrl/SHA256SUMS.txt" -UseBasicParsing).Content
        }
        finally
        {
            $ProgressPreference = $previousProgress
        }

        Write-Step 'Verifying the checksum'
        $expected = $checksums -split "`n" |
            Where-Object { $_ -match [Regex]::Escape($archiveName) } |
            ForEach-Object { ($_.Trim() -split '\s+')[0] } |
            Select-Object -First 1

        if (-not $expected)
        {
            throw "SHA256SUMS.txt from $tag does not list $archiveName."
        }

        $actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash
        if ($actual -ne $expected.ToUpperInvariant())
        {
            throw "Checksum mismatch for ${archiveName}: expected $expected, got $actual. The download has been discarded."
        }

        Stop-RunningGitxen $InstallDir

        if (Test-Path $InstallDir)
        {
            Write-Step 'Removing the previous version'
            Remove-Item -Path $InstallDir -Recurse -Force
        }

        Write-Step "Unpacking into $InstallDir"
        New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null
        Expand-Archive -Path $archive -DestinationPath $InstallDir -Force
    }
    finally
    {
        Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
    }

    $executable = Join-Path $InstallDir $executableName
    if (-not (Test-Path $executable))
    {
        throw "The archive did not contain $executableName, so $InstallDir is not a usable installation."
    }

    # Keep a copy of this script beside the application, so Add/Remove Programs can uninstall
    # without a network round trip.
    $uninstaller = Join-Path $InstallDir 'uninstall.ps1'
    if ($PSCommandPath)
    {
        Copy-Item -Path $PSCommandPath -Destination $uninstaller -Force
    }
    else
    {
        # Started through `irm | iex`, so there is no file on disk to copy: fetch one.
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/$repository/master/setup/gitxen/install.ps1" -OutFile $uninstaller -UseBasicParsing
    }

    if (-not $NoShortcut)
    {
        Write-Step 'Creating the Start menu shortcut'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $executable
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = 'A git client for Windows'
        $shortcut.Save()
    }

    if (-not $NoPathUpdate)
    {
        $entries = Get-UserPathEntries
        if (-not ($entries | Where-Object { $_.TrimEnd('\') -eq $InstallDir.TrimEnd('\') }))
        {
            Write-Step 'Adding the install folder to PATH'
            [Environment]::SetEnvironmentVariable('PATH', ((@($entries) + $InstallDir) -join ';'), 'User')
            # So that `gitxen` also works in the session that ran the installer, not only in new ones.
            $env:PATH = "$env:PATH;$InstallDir"
        }
    }

    Write-Step 'Registering in Add/Remove Programs'
    $sizeInKb = [Math]::Round((Get-ChildItem -Path $InstallDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1KB)
    New-Item -Path $registryKey -Force | Out-Null
    Set-ItemProperty -Path $registryKey -Name 'DisplayName' -Value $displayName
    Set-ItemProperty -Path $registryKey -Name 'DisplayVersion' -Value $Version
    Set-ItemProperty -Path $registryKey -Name 'DisplayIcon' -Value $executable
    Set-ItemProperty -Path $registryKey -Name 'Publisher' -Value 'Kakoso'
    Set-ItemProperty -Path $registryKey -Name 'InstallLocation' -Value $InstallDir
    Set-ItemProperty -Path $registryKey -Name 'URLInfoAbout' -Value "https://github.com/$repository"
    Set-ItemProperty -Path $registryKey -Name 'NoModify' -Value 1 -Type DWord
    Set-ItemProperty -Path $registryKey -Name 'NoRepair' -Value 1 -Type DWord
    Set-ItemProperty -Path $registryKey -Name 'EstimatedSize' -Value $sizeInKb -Type DWord
    Set-ItemProperty -Path $registryKey -Name 'UninstallString' `
        -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`" -Uninstall"

    Write-Host ''
    Write-Host "$displayName $Version is installed in $InstallDir." -ForegroundColor Green
    Write-Host "Start it from the Start menu, or run 'gitxen' in a new terminal." -ForegroundColor Green
}

if ($Uninstall)
{
    Remove-Installation
}
else
{
    Install-Gitxen
}
