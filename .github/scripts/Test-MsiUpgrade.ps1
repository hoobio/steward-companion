#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Msi,
    [Parameter(Mandatory)] [string] $OutDir
)

$ErrorActionPreference = 'Stop'

$install = Join-Path $env:LOCALAPPDATA 'Steward'
$exe = Join-Path $install 'Steward.App.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$upgradeCode = '{CCD0BF88-7A8E-4F74-9DB7-9B9272B3D503}'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
Add-Type -AssemblyName System.Drawing, System.Windows.Forms

function Save-Screen([string] $Name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save((Join-Path $OutDir "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Install-Msi([string] $Path, [string] $Log) {
    $p = Start-Process msiexec.exe -ArgumentList @('/i', "`"$Path`"", '/qn', '/l*v', "`"$Log`"") -Wait -PassThru
    if ($p.ExitCode -notin 0, 3010) { throw "msiexec /i $(Split-Path $Path -Leaf) failed (exit $($p.ExitCode)); see $Log" }
}

function Wait-StewardWindow {
    $deadline = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Seconds 1
        $process = Get-Process -Name Steward.App -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
    } until ($process -or (Get-Date) -gt $deadline)
    if (-not $process) {
        Get-Process -Name Steward* -ErrorAction SilentlyContinue | Format-Table Id, ProcessName, Path, MainWindowHandle, StartTime | Out-String | Write-Host
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-5) } -ErrorAction SilentlyContinue |
            Where-Object { $_.LevelDisplayName -in 'Error', 'Critical' -or $_.ProviderName -match '\.NET|Application Error|Windows Error Reporting' } |
            Format-List TimeCreated, ProviderName, Message | Out-String -Width 400 | Write-Host
        throw 'No Steward.App window within 90 seconds of the install'
    }
    if ($process.Path -ne $exe) { throw "The window is from $($process.Path), not $exe" }
    $process
}

function Get-StewardProduct {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    @($installer.RelatedProducts($upgradeCode) | ForEach-Object {
            [pscustomobject]@{ ProductCode = $_; DisplayVersion = $installer.ProductInfo($_, 'VersionString') }
        })
}

$old = Join-Path $env:RUNNER_TEMP 'previous'
$tag = gh release view --repo $env:GITHUB_REPOSITORY --json tagName --jq .tagName
if ($LASTEXITCODE -ne 0) { throw "gh release view failed (exit $LASTEXITCODE)" }
gh release download $tag --repo $env:GITHUB_REPOSITORY --pattern '*-x64.msi' --dir $old --clobber
if ($LASTEXITCODE -ne 0) { throw "gh release download $tag failed (exit $LASTEXITCODE)" }
$oldMsi = (Get-ChildItem $old -Filter *.msi | Select-Object -First 1).FullName
Write-Host "Installing the released $tag MSI: $oldMsi"

try {
    Install-Msi $oldMsi (Join-Path $OutDir 'install-previous.log')
    $previous = Wait-StewardWindow
    Write-Host "Previous build: '$($previous.MainWindowTitle)' from $($previous.Path), $((Get-Item $exe).VersionInfo.FileVersion)"
    Save-Screen 'previous'
    if (-not (Test-Path $runKey)) { New-Item -Path $runKey | Out-Null }
    Set-ItemProperty -Path $runKey -Name Steward -Value "`"$exe`" --tray"
    $before = Get-StewardProduct
    if (-not $before) { throw "The released $tag MSI registered no Steward product" }
    Write-Host "Before the upgrade: $($before.ProductCode -join ', ')"

    # The in-app MSI update runs msiexec only after Steward exits; a same-train launch while it still runs hands over to it and exits.
    Stop-Process -Id $previous.Id -Force
    Wait-Process -Id $previous.Id -Timeout 30 -ErrorAction SilentlyContinue
    Install-Msi $Msi (Join-Path $OutDir 'install-upgrade.log')
    $current = Wait-StewardWindow
    if ($current.Id -eq $previous.Id) { throw 'The previous build is still the running Steward after the upgrade' }
    Write-Host "Upgraded build: '$($current.MainWindowTitle)' from $($current.Path)"
    Start-Sleep -Seconds 5
    Save-Screen 'login-screen-msi'

    $after = Get-StewardProduct
    if ($after.Count -ne 1) { throw "Expected one installed Steward product after the upgrade, found $($after.Count): $($after.ProductCode -join ', ')" }
    if ($after.ProductCode -in $before.ProductCode) { throw "The released product $($after.ProductCode) is still the installed one" }
    if (-not (Test-Path (Join-Path $install 'Steward.Desktop.dll'))) { throw 'Steward.Desktop.dll is not in the install folder' }
    $leftovers = @(Get-ChildItem $install -Filter 'Microsoft.WinUI.dll' -Recurse -ErrorAction SilentlyContinue)
    if ($leftovers) { throw "WinUI files left after the upgrade: $($leftovers.FullName -join ', ')" }
    $run = (Get-ItemProperty -Path $runKey -Name Steward).Steward
    if ($run -notmatch '^"([^"]+)"' -or -not (Test-Path $Matches[1])) { throw "The Run value no longer starts an existing file: $run" }
    $shortcut = Get-ChildItem (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs') -Filter 'Steward.lnk' -Recurse | Select-Object -First 1
    if (-not $shortcut) { throw 'No Steward Start menu shortcut after the upgrade' }
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut.FullName).TargetPath
    if ($target -ne $exe) { throw "The Start menu shortcut starts $target, not $exe" }

    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 1
        $started = Get-ChildItem (Join-Path $install 'logs') -Filter 'steward-msi-*.log' -ErrorAction SilentlyContinue |
            Select-String -Pattern 'INF Steward\.Desktop\.App: Steward .* starting \(msi,' | Select-Object -First 1
    } until ($started -or (Get-Date) -gt $deadline)
    if (-not $started) { throw 'No "starting (msi," line in the MSI log after the upgrade' }
    Write-Host "Startup line: $($started.Line)"
    Write-Host "Run value: $run; shortcut: $target; product: $($after.ProductCode) $($after.DisplayVersion)"
}
finally {
    Copy-Item (Join-Path $install 'logs') (Join-Path $OutDir 'logs') -Recurse -ErrorAction SilentlyContinue
    Get-Process -Name Steward.App -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    foreach ($product in Get-StewardProduct) {
        Start-Process msiexec.exe -ArgumentList @('/x', $product.ProductCode, '/qn') -Wait
    }
    Remove-ItemProperty -Path $runKey -Name Steward -ErrorAction SilentlyContinue
}
