using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Steward.App.Services;

public sealed partial class TintedAcrylicBackdrop : SystemBackdrop
{
    private readonly Dictionary<ICompositionSupportsSystemBackdrop, DesktopAcrylicController> _controllers = [];

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        var controller = new DesktopAcrylicController
        {
            TintColor = PaletteColor("FlyoutAcrylicTintColor"),
            FallbackColor = PaletteColor("FlyoutAcrylicFallbackColor"),
            TintOpacity = 0.15f,
            LuminosityOpacity = 0.96f,
        };
        controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
        controller.AddSystemBackdropTarget(connectedTarget);
        _controllers[connectedTarget] = controller;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_controllers.Remove(disconnectedTarget, out var controller))
        {
            controller.RemoveSystemBackdropTarget(disconnectedTarget);
            controller.Dispose();
        }
    }

    private static Color PaletteColor(string key) => (Color)Application.Current.Resources[key];
}
