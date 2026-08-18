$ErrorActionPreference = 'Stop'

# The MSI is downloaded from the GitHub release rather than embedded: Chocolatey's package size
# limit is far below the ~300 MB of a self-contained Windows App SDK payload.
$packageArgs = @{
    packageName    = $env:ChocolateyPackageName
    fileType       = 'msi'
    url64bit       = '{{MSI_URL}}'
    checksum64     = '{{MSI_SHA256}}'
    checksumType64 = 'sha256'
    silentArgs     = '/qn /norestart'
    # 3010 and 1641 are "success, reboot required" -- neither is a failure.
    validExitCodes = @(0, 3010, 1641)
    softwareName   = 'Gitxen*'
}

Install-ChocolateyPackage @packageArgs
