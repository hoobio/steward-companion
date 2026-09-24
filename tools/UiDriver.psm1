Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class UiNative {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, int e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int op);
}
"@
[UiNative]::SetProcessDPIAware() | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Start-UiApp {
    param([Parameter(Mandatory)][string]$Path, [string[]]$ArgumentList = @(), [int]$SettleSeconds = 4)
    $process = if ($ArgumentList) { Start-Process $Path -ArgumentList $ArgumentList -PassThru } else { Start-Process $Path -PassThru }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($clock.Elapsed.TotalSeconds -gt 30) { throw "No window from $Path after 30s" }
        Start-Sleep -Milliseconds 300
        $process.Refresh()
    } until ($process.MainWindowHandle -ne 0)
    Start-Sleep $SettleSeconds
    [UiNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    [pscustomobject]@{ Process = $process; Window = $AE::FromHandle($process.MainWindowHandle) }
}

function Get-UiPopup {
    param([Parameter(Mandatory)]$App)
    $byProcess = [System.Windows.Automation.PropertyCondition]::new($AE::ProcessIdProperty, $App.Process.Id)
    $AE::RootElement.FindAll($TS::Children, $byProcess) |
        Where-Object { $_.Current.NativeWindowHandle -ne $App.Process.MainWindowHandle } |
        Select-Object -First 1
}

function Find-UiElement {
    param([Parameter(Mandatory)]$Root, [Parameter(Mandatory)][scriptblock]$Where, [int]$TimeoutSeconds = 15)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        foreach ($element in $Root.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            if (-not $element.Current.BoundingRectangle.IsEmpty -and (& $Where $element)) { return $element }
        }
        Start-Sleep -Milliseconds 300
    }
    throw "No element matched: $Where"
}

function Get-UiCentre {
    param([Parameter(Mandatory)]$Element)
    $bounds = $Element.Current.BoundingRectangle
    [int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2)
}

# WinUI ignores SetCursorPos for hover; only injected input raises pointer events.
function Move-UiPointer {
    param([int]$X, [int]$Y)
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    [UiNative]::mouse_event(0xC001, [int](($X - $screen.X) * 65535 / ($screen.Width - 1)), [int](($Y - $screen.Y) * 65535 / ($screen.Height - 1)), 0, 0)
}

function Invoke-UiClick {
    param([int]$X, [int]$Y)
    Move-UiPointer $X $Y
    [UiNative]::mouse_event(2, 0, 0, 0, 0)
    [UiNative]::mouse_event(4, 0, 0, 0, 0)
}

# CAPTUREBLT is required: windowed popups (menus, flyouts with a system backdrop) are layered windows.
function Copy-UiScreen {
    param([Parameter(Mandatory)][System.Windows.Rect]$Rect)
    $bitmap = [System.Drawing.Bitmap]::new([int]$Rect.Width, [int]$Rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $source = [UiNative]::GetDC([IntPtr]::Zero)
    $target = $graphics.GetHdc()
    [UiNative]::BitBlt($target, 0, 0, $bitmap.Width, $bitmap.Height, $source, [int]$Rect.X, [int]$Rect.Y, 0x40CC0020) | Out-Null
    $graphics.ReleaseHdc($target)
    [UiNative]::ReleaseDC([IntPtr]::Zero, $source) | Out-Null
    $graphics.Dispose()
    $bitmap
}

function Save-UiScreenshot {
    param([Parameter(Mandatory)][System.Windows.Rect]$Rect, [Parameter(Mandatory)][string]$Path)
    $bitmap = Copy-UiScreen $Rect
    $bitmap.Save($Path)
    $bitmap.Dispose()
}

function Measure-UiTransition {
    param(
        [Parameter(Mandatory)][System.Windows.Rect]$Rect,
        [Parameter(Mandatory)][int]$SampleX,
        [Parameter(Mandatory)][int]$SampleY,
        [Parameter(Mandatory)][int]$PointerX,
        [Parameter(Mandatory)][int]$PointerY,
        [int]$Milliseconds = 250,
        [string]$SavePrefix
    )
    $frames = [System.Collections.Generic.List[object]]::new()
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Move-UiPointer $PointerX $PointerY
    while ($clock.ElapsedMilliseconds -lt $Milliseconds) {
        $frames.Add([pscustomobject]@{ Ms = $clock.Elapsed.TotalMilliseconds; Bitmap = Copy-UiScreen $Rect })
    }
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $pixel = $frames[$i].Bitmap.GetPixel($SampleX, $SampleY)
        if ($SavePrefix -and $i % 3 -eq 0) { $frames[$i].Bitmap.Save("$SavePrefix-$i.png") }
        $frames[$i].Bitmap.Dispose()
        [pscustomobject]@{ Frame = $i; Ms = [Math]::Round($frames[$i].Ms, 1); Colour = '#{0:X2}{1:X2}{2:X2}' -f $pixel.R, $pixel.G, $pixel.B; Luma = [int](($pixel.R + $pixel.G + $pixel.B) / 3) }
    }
}

Export-ModuleMember -Function Start-UiApp, Get-UiPopup, Find-UiElement, Get-UiCentre, Move-UiPointer, Invoke-UiClick, Copy-UiScreen, Save-UiScreenshot, Measure-UiTransition
