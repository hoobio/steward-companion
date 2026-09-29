using System.Runtime.InteropServices;

using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Steward.App.Services;

public sealed partial class TintedAcrylicBackdrop : SystemBackdrop
{
    private const string PopupWindowClass = "Microsoft.UI.Content.PopupWindowSiteBridge";
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;
    private const int DwmBorderColor = 34;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    private readonly Dictionary<ICompositionSupportsSystemBackdrop, (DesktopAcrylicController Controller, SystemBackdropConfiguration Configuration)> _controllers = [];

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        var controller = new DesktopAcrylicController
        {
            TintColor = PaletteColor("FlyoutAcrylicTintColor"),
            FallbackColor = PaletteColor("FlyoutAcrylicFallbackColor"),
            TintOpacity = 0.8f,
            LuminosityOpacity = 0.9f,
        };
        // A windowed popup is never the input-active window, and the default configuration then shows the opaque fallback.
        var configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark };
        controller.SetSystemBackdropConfiguration(configuration);
        controller.AddSystemBackdropTarget(connectedTarget);
        _controllers[connectedTarget] = (controller, configuration);
        RoundPopupWindows();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_controllers.Remove(disconnectedTarget, out var entry))
        {
            entry.Controller.RemoveSystemBackdropTarget(disconnectedTarget);
            entry.Controller.Dispose();
        }
    }

    private static Color PaletteColor(string key) => (Color)Application.Current.Resources[key];

    // The backdrop fills the popup HWND's rectangle, so the presenter's corner radius alone leaves square corners.
    private static void RoundPopupWindows()
    {
        var processId = (uint)Environment.ProcessId;
        for (var hwnd = FindWindowEx(0, 0, PopupWindowClass, null); hwnd != 0; hwnd = FindWindowEx(0, hwnd, PopupWindowClass, null))
        {
            if (GetWindowThreadProcessId(hwnd, out var owner) != 0 && owner == processId)
            {
                var preference = DwmCornerRound;
                _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref preference, sizeof(int));
                var border = DwmColorNone;
                _ = DwmSetWindowAttribute(hwnd, DwmBorderColor, ref border, sizeof(int));
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
