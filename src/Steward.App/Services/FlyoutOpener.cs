using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

using Steward.Core.Diagnostics;

namespace Steward.App.Services;

internal static class FlyoutOpener
{
    private static readonly TimeSpan ActivationSettle = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);
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

    public static void Attach(ButtonBase button, FlyoutBase flyout, string name)
    {
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

            Open(button, flyout, name);
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

    private static void ClearPress(object sender, PointerRoutedEventArgs e) => _pressedWhileActivating = false;

    private static void Open(FrameworkElement target, FlyoutBase flyout, string name)
    {
        var defer = _pressedWhileActivating;
        _pressedWhileActivating = false;
        if (!defer)
        {
            flyout.ShowAt(target);
            return;
        }

        Log(name, "Deferred");
        target.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => flyout.ShowAt(target));
    }

    private static void Log(string name, string stage) =>
        _logger?.Info($"Flyout {name} {stage} windowActive={_windowActive} sinceActivationMs={SinceActivationMs}");
}
