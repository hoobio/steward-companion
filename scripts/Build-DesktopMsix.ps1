#Requires -Version 7.0

<#
.SYNOPSIS
Builds the Avalonia app (Steward.Desktop) as an unsigned x64 MSIX layout, .msix and .msixupload.
#>
[CmdletBinding()]
param(
    [ValidateSet('Store', 'Dev')]
    [string] $Identity = 'Store',

    [ValidatePattern('^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$')]
    [string] $Version,

    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'Steward.DesktopMsix'),

    [string] $StoreDisplayVersion
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\Steward.Desktop\Steward.Desktop.csproj'
$manifestSource = Join-Path $repoRoot 'packaging\windows\Package.appxmanifest'
$assetSource = Join-Path $repoRoot 'src\Steward.App\Assets'

function Find-SdkTool {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Name)

    $props = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Packages.props') -Raw)
    $buildToolsVersion = ($props.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.Windows.SDK.BuildTools').Version
    $packagesRoot = $env:NUGET_PACKAGES ?? (Join-Path $env:USERPROFILE '.nuget\packages')
    $candidates = @(
        Get-ChildItem -Path (Join-Path $packagesRoot "microsoft.windows.sdk.buildtools\$buildToolsVersion\bin") -Recurse -Filter $Name -ErrorAction SilentlyContinue
        Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter $Name -ErrorAction SilentlyContinue |
            Sort-Object { [version]($_.FullName -replace '.*\\(\d+\.\d+\.\d+\.\d+)\\.*', '$1') } -Descending
    ) | Where-Object { $_.FullName -match '\\x64\\[^\\]+$' }
    $tool = $candidates | Select-Object -First 1
    if (-not $tool) { throw "$Name was found neither in Microsoft.Windows.SDK.BuildTools $buildToolsVersion nor in an installed Windows SDK" }
    Write-Verbose "Using $($tool.FullName)"
    $tool.FullName
}

function Invoke-Tool {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string[]] $Arguments)

    & $Path @Arguments | Write-Verbose
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $Path -Leaf) $($Arguments[0]) failed (exit $LASTEXITCODE)" }
}

$versionText = (Get-Content -LiteralPath (Join-Path $repoRoot 'version.txt') -Raw).Trim()
if ($versionText -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "version.txt does not hold a three-part version: '$versionText'" }
$majorMinor = "$($Matches[1]).$($Matches[2])"
if (-not $Version) {
    $Version = if ($Identity -eq 'Dev') {
        $commitCount = & git -C $repoRoot rev-list --count HEAD
        if ($LASTEXITCODE -ne 0) { throw "git rev-list failed (exit $LASTEXITCODE)" }
        "$versionText.$([Math]::Min([int]$commitCount, 65535))"
    }
    else {
        "$majorMinor.$([Math]::Min([int]($env:GITHUB_RUN_NUMBER ?? 0), 65535)).0"
    }
}
if ($Identity -eq 'Store' -and $Version -notmatch '\.0$') { throw "A Store package version must end in .0: $Version" }

$makeAppx = Find-SdkTool 'makeappx.exe'
$makePri = Find-SdkTool 'makepri.exe'

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$layout = Join-Path $OutputDirectory 'layout'
$priRoot = Join-Path $OutputDirectory 'pri'
foreach ($dir in $layout, $priRoot) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}
New-Item -ItemType Directory -Path $layout, (Join-Path $priRoot 'Assets') -Force | Out-Null

$publishArgs = @(
    'publish', $projectPath,
    '-c', 'Release',
    '-f', 'net10.0-windows10.0.26100.0',
    '-r', 'win-x64',
    '--self-contained',
    '-p:PublishSingleFile=false',
    "-p:GitHubRelease=$(($Identity -eq 'Store').ToString().ToLowerInvariant())",
    '-o', $layout
)
if ($StoreDisplayVersion) { $publishArgs += "-p:StoreDisplayVersion=$StoreDisplayVersion" }
Write-Verbose "Publishing Steward.Desktop ($Identity, $Version) to $layout"
$attempt = 0
do {
    $attempt++
    & dotnet @publishArgs | Write-Verbose
    if ($LASTEXITCODE -eq 0) { break }
    Write-Warning "dotnet publish failed (exit $LASTEXITCODE), attempt $attempt of 3"
} while ($attempt -lt 3)
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
if (-not (Test-Path -LiteralPath (Join-Path $layout 'Steward.Desktop.exe'))) { throw 'The publish produced no Steward.Desktop.exe' }

$visualAssets = Get-ChildItem -LiteralPath $assetSource -File -Filter *.png |
    Where-Object Name -match '^(SplashScreen|Square150x150Logo|Square44x44Logo|StoreLogo|Wide310x150Logo)[.]'
New-Item -ItemType Directory -Path (Join-Path $layout 'Assets') -Force | Out-Null
$visualAssets | Copy-Item -Destination (Join-Path $layout 'Assets')
$visualAssets | Copy-Item -Destination (Join-Path $priRoot 'Assets')

$manifest = [IO.File]::ReadAllText($manifestSource)
$stamped = $manifest -replace '(?<=<Identity\s[^>]*)Version="[\d.]+"', "Version=`"$Version`""
if ($Identity -eq 'Dev') {
    $stamped = $stamped `
        -replace '(?<=<Identity\s[^>]*)Name="Hoobi\.Steward"', 'Name="Hoobi.Steward.Dev"' `
        -replace '(?<=<Identity\s[^>]*)Publisher="CN=D74C026B-1081-4787-BDE6-0CFA2F1EDD71"', 'Publisher="CN=Hoobi Dev"' `
        -replace '(?<=<(?:uap:)?DisplayName>)Steward(?=</)', 'Steward (Development)' `
        -replace '(?<=<uap:VisualElements\s[^>]*)DisplayName="Steward"', 'DisplayName="Steward (Development)"'
    if ($stamped -notmatch 'Name="Hoobi\.Steward\.Dev"' -or $stamped -notmatch 'CN=Hoobi Dev') { throw 'The Dev identity stamp made no change; the identity markers in Package.appxmanifest moved' }
}
$manifestPath = Join-Path $layout 'AppxManifest.xml'
[IO.File]::WriteAllText($manifestPath, $stamped, [Text.UTF8Encoding]::new($false))

$priConfig = Join-Path $OutputDirectory 'priconfig.xml'
Invoke-Tool $makePri @('createconfig', '/cf', $priConfig, '/dq', 'en-US', '/pv', '10.0.0', '/o')
$priXml = [xml](Get-Content -LiteralPath $priConfig -Raw)
$priXml.SelectNodes('//packaging') | ForEach-Object { $_.ParentNode.RemoveChild($_) | Out-Null }
$priXml.Save($priConfig)
Invoke-Tool $makePri @('new', '/pr', $priRoot, '/cf', $priConfig, '/mn', $manifestPath, '/of', (Join-Path $layout 'resources.pri'), '/o')

$msix = Join-Path $OutputDirectory "Steward-$Version-x64.msix"
Invoke-Tool $makeAppx @('pack', '/d', $layout, '/p', $msix, '/o')

$upload = Join-Path $OutputDirectory "Steward-$Version-$Identity-x64.msixupload"
$zip = [IO.Path]::ChangeExtension($upload, '.zip')
Compress-Archive -LiteralPath $msix -DestinationPath $zip -Force
Move-Item -LiteralPath $zip -Destination $upload -Force

[pscustomobject]@{
    Identity   = $Identity
    Version    = $Version
    Layout     = $layout
    Msix       = $msix
    MsixUpload = $upload
}
