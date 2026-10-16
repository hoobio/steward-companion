using Avalonia.Controls;
using Avalonia.Threading;

namespace Steward.Desktop.Services;

public static class TrayMenuFocus
{
    private static bool _attached;

    // Avalonia's Win32 TrayIconImpl.OnRightClicked shows the menu presenter in a window without focusing it, so Escape and the arrow keys stop at the window and never reach the menu's key handler.
    public static void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (window.Name?.StartsWith("AvaloniaTrayPopupRoot_", StringComparison.Ordinal) == true && window.Content is MenuFlyoutPresenter presenter)
            {
                presenter.Focusable = true;
                window.Activated += (_, _) => Dispatcher.UIThread.Post(() => presenter.Focus());
            }
        });
    }
}
