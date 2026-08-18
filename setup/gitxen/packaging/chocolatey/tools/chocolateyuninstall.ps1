$ErrorActionPreference = 'Stop'

# Chocolatey's auto-uninstaller normally handles an MSI on its own; this runs the same removal
# explicitly so the package still uninstalls cleanly where that feature is switched off.
$key = Get-UninstallRegistryKey -SoftwareName 'Gitxen*'

if (-not $key)
{
    Write-Host 'Gitxen is not present in the list of installed programs; nothing to uninstall.'
    return
}

if ($key.Count -gt 1)
{
    Write-Warning "$($key.Count) matching installs found; skipping uninstall so the wrong one is not removed:"
    $key | ForEach-Object { Write-Warning "  $($_.DisplayName) $($_.DisplayVersion)" }
    return
}

Uninstall-ChocolateyPackage -PackageName $env:ChocolateyPackageName `
                            -FileType 'msi' `
                            -SilentArgs "$($key.PSChildName) /qn /norestart" `
                            -ValidExitCodes @(0, 3010, 1641) `
                            -File ''
