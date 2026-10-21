using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;

using Steward.Core.Diagnostics;
using Steward.Desktop.Services;

namespace Steward.Desktop.Views;

public sealed partial class MainWindow
{
    private const double MacTrafficLightsWidth = 78;
    private const double MacTitleBarHeight = 28;
    private const double LinuxResizeBorder = 4;
    private const double TitleBarDragThreshold = 4;

    private PointerPressedEventArgs? _titleBarPress;
    private Point _titleBarPressAt;

    partial void ConfigurePlatformWindow()
    {
        Root.Margin = default;
        if (OperatingSystem.IsMacOS())
        {
            TitleArea.Margin = new Thickness(MacTrafficLightsWidth, 0, 0, 0);
            // Avalonia.Native 12.1.3 keeps the traffic lights in AppKit's standard 28px title bar whatever the height hint, so the icon and title centre on that row.
            TitleArea.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            TitleArea.Height = MacTitleBarHeight;
            AppTitleBar.ColumnDefinitions[3].Width = new GridLength(0);
        }
        else
        {
            // Without a compositor the drawn shadow band is opaque, so the window edge keeps only the frame's resize grips.
            WindowDecorationsTheme = new ControlTheme(typeof(WindowDrawnDecorations))
            {
                BasedOn = WindowDecorationsTheme,
                Setters =
                {
                    new Setter(WindowDrawnDecorations.DefaultShadowThicknessProperty, default(Thickness)),
                    new Setter(WindowDrawnDecorations.DefaultFrameThicknessProperty, new Thickness(LinuxResizeBorder)),
                },
            };

            // X11 starts a window-manager move on every press over a TitleBar role before the app sees it, so a double-click never reaches the title row.
            WindowDecorationProperties.SetElementRole(AppTitleBar, WindowDecorationsElementRole.User);
            AppTitleBar.PointerPressed += OnTitleBarPointerPressed;
            AppTitleBar.PointerMoved += OnTitleBarPointerMoved;
            AppTitleBar.PointerReleased += (_, _) => _titleBarPress = null;
            // X11 raises Activated before IsActive changes, so the caption buttons read the state once the event has run.
            EventHandler updateCaptionButtons = (_, _) => Dispatcher.UIThread.Post(UpdateCaptionButtons, DispatcherPriority.Loaded);
            Opened += updateCaptionButtons;
            Activated += updateCaptionButtons;
            Deactivated += updateCaptionButtons;
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && IsVisible)
                {
                    Dispatcher.UIThread.Post(ReadActivationFromX, DispatcherPriority.Background);
                }
            };
            _logger.Info(TrayHost.IsAvailable ? "System tray host found" : "No system tray host on the session bus; close and minimise to tray are off until one starts");
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += (_, _) =>
            {
                _logger.Info("Session ending; quitting");
                _quitting = true;
            };
        }

        if (Avalonia.Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen)
                {
                    ShowFromTray();
                }
            };
        }
    }

    private void ReadActivationFromX()
    {
        if (OperatingSystem.IsLinux() && X11ActiveWindow.ActivateIfActive(this))
        {
            _logger.Info("Window active per _NET_ACTIVE_WINDOW at show; activation applied");
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _titleBarPress = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        _titleBarPress = e;
        _titleBarPressAt = e.GetPosition(this);
    }

    // A window-manager move started on the press swallows the second click of a double-click, so the move waits for the pointer to travel.
    private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_titleBarPress is not { } press)
        {
            return;
        }

        var delta = e.GetPosition(this) - _titleBarPressAt;
        if (Math.Abs(delta.X) < TitleBarDragThreshold && Math.Abs(delta.Y) < TitleBarDragThreshold)
        {
            return;
        }

        _titleBarPress = null;
        // _NET_WM_MOVERESIZE anchors the move at the pointer's current position, so the window first catches up with the travel since the press.
        Position += new PixelPoint((int)Math.Round(delta.X * RenderScaling), (int)Math.Round(delta.Y * RenderScaling));
        BeginMoveDrag(press);
    }
}
