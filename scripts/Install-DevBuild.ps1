#Requires -Version 7.0

<#
.SYNOPSIS
Builds HEAD as a side-by-side "Steward (Development)" MSIX and registers it.

.DESCRIPTION
Exports the pushed HEAD commit to a scratch source folder with git archive, stamps a dev
package identity into that copy's Package.appxmanifest, builds a Release x64 MSIX layout
via dotnet publish (the same flags build-msix uses in .github/workflows/build.yaml) from
that folder, copies the layout to a stable folder outside the repo, and registers it with
Add-AppxPackage -Register: a loose, unsigned layout that Developer Mode accepts without
a certificate. The working tree is never touched, so git status stays clean throughout.
Runs side by side with the Store package (Hoobi.Steward) under a distinct identity,
Hoobi.Steward.Dev.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$devRoot = Join-Path $env:LOCALAPPDATA 'Steward.Dev'
$srcDir = Join-Path $devRoot 'src'
$projectPath = Join-Path $srcDir 'src\Steward.App\Steward.App.csproj'
$manifestPath = Join-Path $srcDir 'src\Steward.App\Package.appxmanifest'
$versionPath = Join-Path $srcDir 'version.txt'
$appDir = Join-Path $devRoot 'app'
$logPath = Join-Path $devRoot 'install.log'
$devPackageName = 'Hoobi.Steward.Dev'

New-Item -ItemType Directory -Path $devRoot -Force | Out-Null
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Get-Process -Name 'Steward.App' -ErrorAction SilentlyContinue |
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

    foreach ($path in @($projectPath, $manifestPath, $versionPath)) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Required path not found: $path" }
    }

    $versionText = (Get-Content -LiteralPath $versionPath -Raw).Trim()
    if ($versionText -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "version.txt does not hold a numeric version: '$versionText'" }
    $fourPartVersion = if ($versionText -match '^\d+\.\d+\.\d+$') { "$versionText.0" } else { $versionText }

    $originalManifest = [System.IO.File]::ReadAllText($manifestPath)
    $stampedManifest = $originalManifest `
        -replace '(?<=<Identity\s[^>]*)Name="Hoobi\.Steward"', 'Name="Hoobi.Steward.Dev"' `
        -replace '(?<=<Identity\s[^>]*)Publisher="CN=D74C026B-1081-4787-BDE6-0CFA2F1EDD71"', 'Publisher="CN=Hoobi Dev"' `
        -replace '(?<=<Identity\s[^>]*)Version="[\d.]+"', "Version=`"$fourPartVersion`"" `
        -replace '<DisplayName>Steward</DisplayName>', '<DisplayName>Steward (Development)</DisplayName>' `
        -replace '(?<=<uap:VisualElements\s[^>]*)DisplayName="Steward"', 'DisplayName="Steward (Development)"'
    if ($stampedManifest -eq $originalManifest) { throw 'Manifest stamp made no change; the identity markers in Package.appxmanifest moved' }
    [System.IO.File]::WriteAllText($manifestPath, $stampedManifest, [System.Text.UTF8Encoding]::new($false))

    $publishDir = Join-Path $env:TEMP 'Steward.Dev.Publish'
    if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }

    Write-Information "Restoring $projectPath (x64)" -InformationAction Continue
    & dotnet restore $projectPath -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed (exit $LASTEXITCODE)" }

    Write-Information "Building $projectPath (x64/Release)" -InformationAction Continue
    & dotnet build $projectPath -c Release -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

    Write-Information "Publishing dev MSIX layout $fourPartVersion to $publishDir" -InformationAction Continue
    & dotnet publish $projectPath `
        -c Release `
        -p:Platform=x64 `
        -p:WindowsPackageType=MSIX `
        -p:WindowsAppSDKSelfContained=false `
        -p:AppxPackageDir="$publishDir\" `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxBundle=Never
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (MSIX) failed (exit $LASTEXITCODE)" }

    $msix = Get-ChildItem -Path $publishDir -Filter '*.msix' -Recurse | Select-Object -First 1
    if (-not $msix) { throw "No .msix produced under '$publishDir'" }

    Write-Information "Extracting $($msix.FullName) to $appDir" -InformationAction Continue
    if (Test-Path -LiteralPath $appDir) { Remove-Item -LiteralPath $appDir -Recurse -Force }
    Expand-Archive -LiteralPath $msix.FullName -DestinationPath $appDir -Force
    if (-not (Test-Path -LiteralPath (Join-Path $appDir 'AppxManifest.xml'))) { throw "Extracted layout at '$appDir' has no AppxManifest.xml" }

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
