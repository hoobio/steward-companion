param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class TrayProbe {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
  public static List<IntPtr> WindowsOf(int pid) {
    var list = new List<IntPtr>();
    EnumWindows((h, p) => { uint owner; GetWindowThreadProcessId(h, out owner); if (owner == pid) list.Add(h); return true; }, IntPtr.Zero);
    return list;
  }
  public static string ClassOf(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  public static string TextOf(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
  public static int OwnerOf(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return (int)p; }
  public static void Key(byte vk, bool extended) {
    uint flags = extended ? 1u : 0u;
    keybd_event(vk, 0, flags, UIntPtr.Zero);
    System.Threading.Thread.Sleep(40);
    keybd_event(vk, 0, flags | 2u, UIntPtr.Zero);
  }
}
"@

function Get-VisibleWindows([int]$ProcessId) {
    foreach ($h in [TrayProbe]::WindowsOf($ProcessId)) {
        if (-not [TrayProbe]::IsWindowVisible($h)) { continue }
        $r = [TrayProbe+RECT]::new()
        [void][TrayProbe]::GetWindowRect($h, [ref]$r)
        [pscustomobject]@{ Handle = $h; Class = [TrayProbe]::ClassOf($h); Title = [TrayProbe]::TextOf($h); Rect = "$($r.L),$($r.T) $($r.R - $r.L)x$($r.B - $r.T)" }
    }
}

function Write-Windows([string]$Label, [int]$ProcessId) {
    $list = @(Get-VisibleWindows $ProcessId)
    Write-Host "${Label}: $($list.Count) visible window(s)"
    $list | ForEach-Object { Write-Host "  $($_.Handle) $($_.Class) '$($_.Title)' $($_.Rect)" }
    $list
}

function Get-Foreground {
    $h = [TrayProbe]::GetForegroundWindow()
    "$h $([TrayProbe]::ClassOf($h)) pid $([TrayProbe]::OwnerOf($h))"
}

function Save-Screen([string]$Name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $path = Join-Path $OutDir "$Name.png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Host "Saved $path"
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
Get-Process Steward.Desktop, Steward.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Remove-Item (Join-Path $env:LOCALAPPDATA 'Steward') -Recurse -Force -ErrorAction SilentlyContinue

# Explorer grants the notify-icon owner foreground rights on a real click; a posted callback carries none, so the runner drops the foreground lock instead.
Write-Host "Foreground lock timeout set to 0: $([TrayProbe]::SystemParametersInfo(0x2001, 0, [IntPtr]::Zero, 2))"
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
[void][TrayProbe]::SetCursorPos([int]($screen.Width / 2), [int]($screen.Height / 2))

$process = Start-Process -FilePath $Exe -ArgumentList '--banners-preview' -PassThru
try {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited) { throw "Harness: Steward exited early with code $($process.ExitCode)" }
    } until ($process.MainWindowHandle -ne 0 -or (Get-Date) -gt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'Harness: Steward showed no window within 60 seconds' }
    Start-Sleep -Seconds 5

    $messageWindow = [TrayProbe]::WindowsOf($process.Id) | Where-Object { [TrayProbe]::ClassOf($_).StartsWith('AvaloniaMessageWindow ') } | Select-Object -First 1
    if (-not $messageWindow) { throw "Harness: no AvaloniaMessageWindow in process $($process.Id)" }
    Write-Host "Tray message window: $messageWindow $([TrayProbe]::ClassOf($messageWindow))"

    $before = @(Write-Windows 'Before the tray callback' $process.Id)
    Write-Host "AllowSetForegroundWindow: $([TrayProbe]::AllowSetForegroundWindow($process.Id))"
    # Avalonia 12.1.3 TrayIconImpl: uCallbackMessage WM_USER+1024, wParam the icon id (1 for the first icon), lParam the mouse message.
    if (-not [TrayProbe]::PostMessage($messageWindow, 0x0800, [IntPtr]1, [IntPtr]0x0205)) { throw 'Harness: PostMessage failed' }

    $menu = $null
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 250
        $menu = Get-VisibleWindows $process.Id | Where-Object { $_.Handle -notin $before.Handle } | Select-Object -First 1
    } until ($menu -or (Get-Date) -gt $deadline)
    if (-not $menu) { throw 'Harness: the tray callback opened no menu window' }
    Write-Host "Menu window: $($menu.Handle) $($menu.Class) $($menu.Rect)"

    Start-Sleep -Seconds 2
    if (-not [TrayProbe]::IsWindowVisible($menu.Handle)) { throw 'Harness: the tray menu closed on its own within 2 seconds, with no input sent' }
    $foreground = [TrayProbe]::GetForegroundWindow()
    Write-Host "Foreground with the menu open: $(Get-Foreground)"
    if ($foreground -ne $menu.Handle) { throw "Harness: the tray menu is not the foreground window, so keys would go elsewhere (foreground: $(Get-Foreground))" }
    Save-Screen 'tray-open'

    [TrayProbe]::Key(0x28, $true)
    Start-Sleep -Milliseconds 600
    Save-Screen 'tray-down'
    if (-not [TrayProbe]::IsWindowVisible($menu.Handle)) { throw 'The tray menu closed on Down' }

    [TrayProbe]::Key(0x1B, $false)
    $deadline = (Get-Date).AddSeconds(5)
    do { Start-Sleep -Milliseconds 250 } until (-not [TrayProbe]::IsWindow($menu.Handle) -or -not [TrayProbe]::IsWindowVisible($menu.Handle) -or (Get-Date) -gt $deadline)
    Save-Screen 'tray-after-escape'
    $after = @(Write-Windows 'After Escape' $process.Id)
    Write-Host "Foreground after Escape: $(Get-Foreground)"
    if ($menu.Handle -in $after.Handle) { throw 'The tray menu stayed open after Escape' }
    Write-Host 'The tray menu closed on Escape'
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.WaitForExit(15000) | Out-Null
    Copy-Item (Join-Path $env:LOCALAPPDATA 'Steward\logs') (Join-Path $OutDir 'logs') -Recurse -ErrorAction SilentlyContinue
}
