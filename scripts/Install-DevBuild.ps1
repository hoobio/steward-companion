#Requires -Version 7.0

<#
.SYNOPSIS
Builds HEAD as the side-by-side "Steward (Development)" package of the Avalonia app and registers it.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$devRoot = Join-Path $env:LOCALAPPDATA 'Steward.Dev'
$srcDir = Join-Path $devRoot 'src'
$buildScript = Join-Path $srcDir 'scripts\Build-DesktopMsix.ps1'
$versionPath = Join-Path $srcDir 'version.txt'
$appDir = Join-Path $devRoot 'app'
$logPath = Join-Path $devRoot 'install.log'
$devPackageName = 'Hoobi.Steward.Dev'

New-Item -ItemType Directory -Path $devRoot -Force | Out-Null
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Get-Process -Name 'Steward.App', 'Steward.Desktop' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($appDir, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Information "Exporting HEAD to $srcDir" -InformationAction Continue
    if (Test-Path -LiteralPath $srcDir) { Remove-Item -LiteralPath $srcDir -Recurse -Force }
    New-Item -ItemType Directory -Path $srcDir -Force | Out-Null
    $archivePath = Join-Path $env:TEMP 'Steward.Dev.HEAD.zip'
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
    & git -C $repoRoot archive --format=zip -o $archivePath HEAD
    if ($LASTEXITCODE -ne 0) { throw "git archive HEAD failed (exit $LASTEXITCODE)" }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $srcDir -Force
    Remove-Item -LiteralPath $archivePath -Force

    foreach ($path in @($buildScript, $versionPath)) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Required path not found: $path" }
    }

    $versionText = (Get-Content -LiteralPath $versionPath -Raw).Trim()
    if ($versionText -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "version.txt does not hold a numeric version: '$versionText'" }
    $commitCount = [Math]::Min([int](& git -C $repoRoot rev-list --count HEAD), 65535)
    if ($LASTEXITCODE -ne 0) { throw "git rev-list failed (exit $LASTEXITCODE)" }
    $fourPartVersion = (($versionText -split '\.')[0..2] -join '.') + ".$commitCount"

    Write-Information "Building the Avalonia dev package $fourPartVersion" -InformationAction Continue
    $package = & $buildScript -Identity Dev -Version $fourPartVersion -OutputDirectory (Join-Path $devRoot 'build')

    $registered = Get-AppxPackage -Name $devPackageName
    if ($registered) {
        Write-Information "Removing $devPackageName $($registered.Version), keeping app data" -InformationAction Continue
        Remove-AppxPackage -Package $registered.PackageFullName -PreserveApplicationData
    }

    Write-Information "Copying $($package.Layout) to $appDir" -InformationAction Continue
    if (Test-Path -LiteralPath $appDir) { Remove-Item -LiteralPath $appDir -Recurse -Force }
    Copy-Item -LiteralPath $package.Layout -Destination $appDir -Recurse
    if (-not (Test-Path -LiteralPath (Join-Path $appDir 'AppxManifest.xml'))) { throw "Layout at '$appDir' has no AppxManifest.xml" }

    Write-Information "Registering $devPackageName from $appDir" -InformationAction Continue
    Add-AppxPackage -Register (Join-Path $appDir 'AppxManifest.xml') -ForceApplicationShutdown

    Write-Information "Registered $devPackageName $fourPartVersion" -InformationAction Continue
}
catch {
    Write-Error "Install-DevBuild failed: $($_.Exception.Message)"
    Stop-Transcript | Out-Null
    exit 1
}

Stop-Transcript | Out-Null
