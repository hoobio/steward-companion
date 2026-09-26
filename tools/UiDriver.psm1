Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class UiNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct POINTER_INFO {
    public uint pointerType, pointerId, frameId, pointerFlags; public IntPtr sourceDevice, hwndTarget;
    public POINT ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
    public uint dwTime, historyCount; public int InputData; public uint dwKeyStates; public ulong PerformanceCount; public int ButtonChangeType;
  }
  [StructLayout(LayoutKind.Sequential)] public struct POINTER_PEN_INFO {
    public POINTER_INFO pointerInfo; public uint penFlags, penMask, pressure, rotation; public int tiltX, tiltY;
  }
  // Size is the union with POINTER_TOUCH_INFO, the larger member.
  [StructLayout(LayoutKind.Explicit, Size = 152)] public struct POINTER_TYPE_INFO {
    [FieldOffset(0)] public uint type; [FieldOffset(8)] public POINTER_PEN_INFO pen;
  }
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr CreateSyntheticPointerDevice(uint type, uint maxCount, uint mode);
  [DllImport("user32.dll", SetLastError = true)] public static extern bool InjectSyntheticPointerInput(IntPtr device, POINTER_TYPE_INFO[] info, uint count);
}
"@
[UiNative]::SetProcessDPIAware() | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$script:Pen = [IntPtr]::Zero

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
            # An element torn down mid-walk (page navigation) throws on property access.
            try { $match = -not $element.Current.BoundingRectangle.IsEmpty -and (& $Where $element) } catch { $match = $false }
            if ($match) { return $element }
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

function Get-UiPattern {
    param([Parameter(Mandatory)]$Element, [Parameter(Mandatory)][type]$Pattern)
    $found = $null
    if ($Element.TryGetCurrentPattern($Pattern::Pattern, [ref]$found)) { $found }
}

function Invoke-UiClick {
    param([Parameter(Mandatory)]$Element)
    $name = $Element.Current.Name
    if ($pattern = Get-UiPattern $Element ([System.Windows.Automation.InvokePattern])) { return $pattern.Invoke() }
    if ($pattern = Get-UiPattern $Element ([System.Windows.Automation.TogglePattern])) { return $pattern.Toggle() }
    if ($pattern = Get-UiPattern $Element ([System.Windows.Automation.SelectionItemPattern])) { return $pattern.Select() }
    if ($pattern = Get-UiPattern $Element ([System.Windows.Automation.ExpandCollapsePattern])) {
        if ($pattern.Current.ExpandCollapseState -eq 'Collapsed') { return $pattern.Expand() }
        return $pattern.Collapse()
    }
    throw "'$name' ($($Element.Current.ControlType.ProgrammaticName)) supports no Invoke, Toggle, SelectionItem or ExpandCollapse pattern"
}

function Write-UiValue {
    param([Parameter(Mandatory)]$Element, [Parameter(Mandatory)][AllowEmptyString()][string]$Value)
    $pattern = Get-UiPattern $Element ([System.Windows.Automation.ValuePattern])
    if (-not $pattern) { throw "'$($Element.Current.Name)' supports no Value pattern" }
    $pattern.SetValue($Value)
}

function Show-UiElement {
    param([Parameter(Mandatory)]$Element)
    $pattern = Get-UiPattern $Element ([System.Windows.Automation.ScrollItemPattern])
    if (-not $pattern) { throw "'$($Element.Current.Name)' supports no ScrollItem pattern" }
    $pattern.ScrollIntoView()
}

function Move-UiScroll {
    param([Parameter(Mandatory)]$Element, [double]$VerticalPercent = -1, [double]$HorizontalPercent = -1)
    $pattern = Get-UiPattern $Element ([System.Windows.Automation.ScrollPattern])
    if (-not $pattern) { throw "'$($Element.Current.Name)' supports no Scroll pattern" }
    $pattern.SetScrollPercent($HorizontalPercent, $VerticalPercent)
}

function Send-UiPen {
    param([int]$X, [int]$Y, [uint32]$Flags)
    if ($script:Pen -eq [IntPtr]::Zero) {
        $script:Pen = [UiNative]::CreateSyntheticPointerDevice(3, 1, 1)
        if ($script:Pen -eq [IntPtr]::Zero) { throw "CreateSyntheticPointerDevice failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    }
    $info = [UiNative+POINTER_TYPE_INFO]::new()
    $info.type = 3
    $info.pen.pointerInfo.pointerType = 3
    $info.pen.pointerInfo.pointerFlags = $Flags
    $info.pen.pointerInfo.ptPixelLocation = [UiNative+POINT]@{ X = $X; Y = $Y }
    if (-not [UiNative]::InjectSyntheticPointerInput($script:Pen, @($info), 1)) { throw "InjectSyntheticPointerInput failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
}

# Pen in range without INCONTACT hovers rather than presses; injection hit-tests on screen, so the target must be the topmost window there.
function Move-UiPointer {
    param([int]$X, [int]$Y)
    Send-UiPen -X $X -Y $Y -Flags 0x20002
}

function Exit-UiPointer {
    param([int]$X, [int]$Y)
    Send-UiPen -X $X -Y $Y -Flags 0x20000
}

function Copy-UiWindow {
    param([Parameter(Mandatory)]$Window)
    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    $rect = [UiNative+RECT]::new()
    if (-not [UiNative]::GetWindowRect($handle, [ref]$rect)) { throw "GetWindowRect failed for '$($Window.Current.Name)'" }
    $bitmap = [System.Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    $printed = [UiNative]::PrintWindow($handle, $dc, 2)
    $graphics.ReleaseHdc($dc)
    $graphics.Dispose()
    if (-not $printed) { $bitmap.Dispose(); throw "PrintWindow failed for '$($Window.Current.Name)'" }
    [pscustomobject]@{ Bitmap = $bitmap; X = $rect.Left; Y = $rect.Top }
}

function Copy-UiComposite {
    param([Parameter(Mandatory)]$Window, $Popup)
    $main = Copy-UiWindow $Window
    if (-not $Popup) { return $main }
    $over = Copy-UiWindow $Popup
    $left = [Math]::Min($main.X, $over.X)
    $top = [Math]::Min($main.Y, $over.Y)
    $right = [Math]::Max($main.X + $main.Bitmap.Width, $over.X + $over.Bitmap.Width)
    $bottom = [Math]::Max($main.Y + $main.Bitmap.Height, $over.Y + $over.Bitmap.Height)
    $bitmap = [System.Drawing.Bitmap]::new($right - $left, $bottom - $top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.DrawImageUnscaled($main.Bitmap, $main.X - $left, $main.Y - $top)
    $graphics.DrawImageUnscaled($over.Bitmap, $over.X - $left, $over.Y - $top)
    $graphics.Dispose()
    $main.Bitmap.Dispose()
    $over.Bitmap.Dispose()
    [pscustomobject]@{ Bitmap = $bitmap; X = $left; Y = $top }
}

function Save-UiScreenshot {
    param([Parameter(Mandatory)]$Window, [Parameter(Mandatory)][string]$Path, $Popup, [System.Windows.Rect]$Rect = [System.Windows.Rect]::Empty)
    $capture = Copy-UiComposite $Window $Popup
    $bitmap = $capture.Bitmap
    if (-not $Rect.IsEmpty) {
        $bitmap = $capture.Bitmap.Clone([System.Drawing.Rectangle]::new([int]$Rect.X - $capture.X, [int]$Rect.Y - $capture.Y, [int]$Rect.Width, [int]$Rect.Height), $capture.Bitmap.PixelFormat)
        $capture.Bitmap.Dispose()
    }
    $bitmap.Save($Path)
    $bitmap.Dispose()
}

function Measure-UiTransition {
    param(
        [Parameter(Mandatory)]$Window,
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
        $frames.Add([pscustomobject]@{ Ms = $clock.Elapsed.TotalMilliseconds; Capture = Copy-UiWindow $Window })
    }
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $capture = $frames[$i].Capture
        $pixel = $capture.Bitmap.GetPixel($SampleX - $capture.X, $SampleY - $capture.Y)
        if ($SavePrefix -and $i % 3 -eq 0) { $capture.Bitmap.Save("$SavePrefix-$i.png") }
        $capture.Bitmap.Dispose()
        [pscustomobject]@{ Frame = $i; Ms = [Math]::Round($frames[$i].Ms, 1); Colour = '#{0:X2}{1:X2}{2:X2}' -f $pixel.R, $pixel.G, $pixel.B; Luma = [int](($pixel.R + $pixel.G + $pixel.B) / 3) }
    }
}

Export-ModuleMember -Function Start-UiApp, Get-UiPopup, Find-UiElement, Get-UiCentre, Invoke-UiClick, Write-UiValue, Show-UiElement, Move-UiScroll, Move-UiPointer, Exit-UiPointer, Copy-UiWindow, Save-UiScreenshot, Measure-UiTransition
