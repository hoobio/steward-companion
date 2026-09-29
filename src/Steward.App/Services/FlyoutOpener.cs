using System.Diagnostics;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Steward.Core.Diagnostics;

using VirtualKey = Windows.System.VirtualKey;

namespace Steward.App.Services;

internal static class FlyoutOpener
{
    private static readonly TimeSpan ActivationSettle = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);
    private static readonly ConditionalWeakTable<FlyoutBase, object?> Attached = [];
    private static ILogger? _logger;
    private static bool _windowActive;
    private static long _activatedAt;
    private static bool _pressedWhileActivating;

    private static bool IsActivating => !_windowActive || Stopwatch.GetElapsedTime(_activatedAt) < ActivationSettle;

    private static int SinceActivationMs => (int)Stopwatch.GetElapsedTime(_activatedAt).TotalMilliseconds;

    public static void TrackActivation(Window window, ILogger logger)
    {
        _logger = logger;
        window.Activated += (_, args) =>
        {
            var active = args.WindowActivationState != WindowActivationState.Deactivated;
            if (active && !_windowActive)
            {
                _activatedAt = Stopwatch.GetTimestamp();
            }

            _windowActive = active;
            logger.Info($"Window activation {args.WindowActivationState}");
        };
    }

    public static void HideAll(XamlRoot? root)
    {
        foreach (var (flyout, _) in Attached.Where(entry => entry.Key.IsOpen))
        {
            flyout.Hide();
        }

        if (root is not null)
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            {
                popup.IsOpen = false;
            }
        }
    }

    public static void Attach(ButtonBase button, FlyoutBase flyout, string name, Func<FlyoutShowOptions>? options = null)
    {
        Attached.AddOrUpdate(flyout, null);
        long closedAt = 0;
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _pressedWhileActivating = IsActivating), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ClearPress), true);
        button.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ClearPress), true);
        button.Click += (_, _) =>
        {
            // The overlay passes the dismissing press through to this button, so the same click that closed the flyout would reopen it.
            if (closedAt != 0 && Stopwatch.GetElapsedTime(closedAt) < ReopenGuard)
            {
                _pressedWhileActivating = false;
                Log(name, "Reopen suppressed");
                return;
            }

            Open(button, flyout, name, options);
        };
        flyout.Opening += (_, _) => Log(name, "Opening");
        flyout.Opened += (_, _) => Log(name, "Opened");
        flyout.Closing += (_, _) => Log(name, "Closing");
        flyout.Closed += (_, _) =>
        {
            closedAt = Stopwatch.GetTimestamp();
            Log(name, "Closed");
        };
    }

    // WinUI's cascading menus read HKCU\Control Panel\Desktop\MenuShowDelay and fall back to 400ms (CascadingMenuHelper.cpp, MenuFlyout_Partial.h).
    private static TimeSpan SubmenuDelay()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return TimeSpan.FromMilliseconds(int.TryParse(key?.GetValue("MenuShowDelay") as string, out var ms) && ms >= 0 ? ms : 400);
    }

    public static void AttachSubmenu(ButtonBase row, Flyout submenu, FrameworkElement submenuContent, IEnumerable<UIElement> otherRows, string name)
    {
        Attach(row, submenu, name);

        var delay = SubmenuDelay();
        var openTimer = row.DispatcherQueue.CreateTimer();
        var closeTimer = row.DispatcherQueue.CreateTimer();
        openTimer.Interval = closeTimer.Interval = delay;
        openTimer.IsRepeating = closeTimer.IsRepeating = false;

        openTimer.Tick += (_, _) =>
        {
            openTimer.Stop();
            if (!submenu.IsOpen)
            {
                submenu.ShowAt(row, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });
            }
        };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            submenu.Hide();
        };

        void ScheduleClose()
        {
            openTimer.Stop();
            if (submenu.IsOpen)
            {
                closeTimer.Start();
            }
        }

        row.PointerEntered += (_, _) =>
        {
            closeTimer.Stop();
            if (!submenu.IsOpen)
            {
                openTimer.Start();
            }
        };
        row.PointerExited += (_, _) => ScheduleClose();
        submenuContent.PointerEntered += (_, _) => closeTimer.Stop();
        foreach (var other in otherRows)
        {
            other.PointerEntered += (_, _) => ScheduleClose();
        }

        row.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Right)
            {
                return;
            }

            e.Handled = true;
            openTimer.Stop();
            closeTimer.Stop();
            submenu.Hide();
            submenu.ShowAt(row);
        };
        submenuContent.KeyDown += (_, e) =>
        {
            if (e.Key is not (VirtualKey.Left or VirtualKey.Escape))
            {
                return;
            }

            e.Handled = true;
            submenu.Hide();
            row.Focus(FocusState.Keyboard);
        };
        submenu.Closed += (_, _) =>
        {
            openTimer.Stop();
            closeTimer.Stop();
        };
    }

    private static void ClearPress(object sender, PointerRoutedEventArgs e) => _pressedWhileActivating = false;

    private static void Open(FrameworkElement target, FlyoutBase flyout, string name, Func<FlyoutShowOptions>? options)
    {
        void Show()
        {
            if (options is null)
            {
                flyout.ShowAt(target);
            }
            else
            {
                flyout.ShowAt(target, options());
            }
        }

        var defer = _pressedWhileActivating;
        _pressedWhileActivating = false;
        if (!defer)
        {
            Show();
            return;
        }

        Log(name, "Deferred");
        target.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Show);
    }

    private static void Log(string name, string stage) =>
        _logger?.Info($"Flyout {name} {stage} windowActive={_windowActive} sinceActivationMs={SinceActivationMs}");
}
