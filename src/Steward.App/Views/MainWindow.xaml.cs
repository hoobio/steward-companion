using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

using H.NotifyIcon.EfficiencyMode;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core.Diagnostics;

using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Graphics;

namespace Steward.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private bool _quitting;
    private bool _confirmingClose;
    private bool _hideWhenMinimized;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _trimTimer;
    private Native.SubclassProc? _sessionEndSubclass;
    private RectInt32 _passthrough;
    private bool _passthroughStale;
    private const double InstallPickerGap = 8;
    private const double AccountFlyoutBottomGap = 8;
    private const double RolePillGap = 8;
    private readonly ILogger<MainWindow> _logger;
    private readonly Microsoft.UI.WindowId _windowId;

    public MainWindow(MainViewModel viewModel, IServiceProvider services, ILogger<MainWindow> logger)
    {
        _services = services;
        _logger = logger;
        InitializeComponent();
        _windowId = AppWindow.Id;
        FlyoutOpener.TrackActivation(this, logger);
        FlyoutOpener.Attach(InstallPicker, InstallFlyout, "install-picker");
        FlyoutOpener.Attach(AccountButton, AccountFlyout, "account", AccountFlyoutAtWindowBottom);
        AccountName.SizeChanged += (_, _) => PlaceRolePill();
        RolePill.SizeChanged += (_, _) => PlaceRolePill();
        FlyoutOpener.AttachSubmenu(GuildRow, GuildFlyout, GuildFlyoutContent, [AccountHeader, OpenGuildPanelButton, AccountSignOutButton], "guild-switcher");
        InstallFlyout.OverlayInputPassThroughElement = InstallPicker;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.SizeChanged += (_, _) => _passthroughStale = true;
        InstallPickerHost.SizeChanged += (_, _) => _passthroughStale = true;
        AppTitleBar.LayoutUpdated += (_, _) =>
        {
            PositionInstallPicker();
            ApplyTitleBarPassthrough(force: false);
        };
        Activated += (_, args) =>
        {
            ApplyTitleBarPassthrough(force: true);
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                _ = ViewModel?.EvaluateCurseForgeDefaultHandlerAsync();
            }
        };
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(App.IconPath);
        ViewModel = viewModel;
        Title = MainViewModel.WindowTitle;
        AppTitleBar.Title = MainViewModel.WindowTitle;
        TrayIcon.ToolTipText = MainViewModel.WindowTitle;
        ViewModel.OwnerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ViewModel.NavigateToAddons = () => Nav.SelectedItem = AddonsItem;
        ViewModel.NavigateToPageTag = tag => Nav.SelectedItem = tag switch
        {
            "sync" => SyncItem,
            "guides" => GuidesItem,
            "settings" => Nav.SettingsItem,
            _ => AddonsItem,
        };
        ViewModel.ShowRestedXpSignIn = () => _ = ShowRestedXpSignInAsync();
        ViewModel.ShowChannelDialog = channel => ShowDialogAsync(new ReleaseChannelDialog(channel));
        ViewModel.ShowConfirmDialog = ConfirmAsync;
        ViewModel.ShowLinkDialog = ShowMessageAsync;
        ViewModel.ChooseLinkFile = ChooseLinkFileAsync;
        ViewModel.ChooseGuild = ChooseGuildAsync;
        ViewModel.QuitRequested = QuitCompletely;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Nav.SelectedItem = AddonsItem;

        var scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(720 * scale);
            presenter.PreferredMinimumHeight = (int)(560 * scale);
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(980 * scale), (int)(720 * scale)));

        // A packaged IconSource resolves asynchronously, so the icon was added with no HICON and the later modify did not repaint it on Windows 10.
        TrayIcon.Icon = new System.Drawing.Icon(App.IconPath, Native.SmallIconSize, Native.SmallIconSize);
        // x:Bind in a Window evaluates only once its content loads, which a --tray launch never does.
        TrayIcon.LeftClickCommand = TrayIcon.DoubleClickCommand = new RelayCommand(ShowFromTray);
        TrayIcon.ForceCreate(enablesEfficiencyMode: false);
        AppWindow.Closing += OnWindowClosing;
        AppWindow.Changed += OnWindowChanged;
        // TextBox handles the tap itself to place the caret, which would clear a selection made on focus.
        SignInUrlBox.AddHandler(UIElement.TappedEvent, new Microsoft.UI.Xaml.Input.TappedEventHandler(SelectAllSignInUrl), true);
        _sessionEndSubclass = OnSessionEndSubclassProc;
        Native.HookSessionEnd(ViewModel.OwnerWindowHandle, _sessionEndSubclass);
    }

    public MainViewModel ViewModel { get; }

    private FlyoutShowOptions AccountFlyoutAtWindowBottom()
    {
        var top = AccountButton.TransformToVisual(null).TransformPoint(default).Y;
        return new FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(AccountButton.ActualWidth, AccountButton.XamlRoot.Size.Height - AccountFlyoutBottomGap - top),
            Placement = FlyoutPlacementMode.RightEdgeAlignedBottom,
        };
    }

    private void PlaceRolePill()
    {
        var beside = AccountName.ActualWidth + RolePillGap + RolePill.ActualWidth <= AccountNameColumn.ActualWidth;
        Grid.SetRow(RolePill, beside ? 0 : 1);
        RolePill.HorizontalAlignment = beside ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        RolePill.VerticalAlignment = beside ? VerticalAlignment.Top : VerticalAlignment.Center;
        RolePill.Margin = beside ? default : new Thickness(0, 8, 0, 0);
    }

    private void PositionInstallPicker()
    {
        var width = InstallPicker.ActualWidth;
        if (_quitting || !InstallPickerHost.IsLoaded || width <= 0 || InstallPickerHost.ActualWidth <= 0)
        {
            return;
        }

        var hostLeft = InstallPickerHost.TransformToVisual(AppTitleBar).TransformPoint(default).X;
        var room = InstallPickerHost.ActualWidth - width;
        var centred = (AppTitleBar.ActualWidth - width) / 2 - hostLeft;
        var x = room >= 2 * InstallPickerGap ? Math.Clamp(centred, InstallPickerGap, room - InstallPickerGap) : Math.Max(0, room);
        if (InstallPickerOffset.X != x)
        {
            InstallPickerOffset.X = x;
        }
    }

    // The TitleBar control sets its own hole over the whole stretched Content on SizeChanged, so ours is reapplied after every layout that moved either.
    private void ApplyTitleBarPassthrough(bool force)
    {
        if (_quitting || !InstallPickerHost.IsLoaded || InstallPickerHost.XamlRoot is not { } root)
        {
            return;
        }

        try
        {
            var rect = default(RectInt32);
            if (InstallPickerHost.Visibility == Visibility.Visible && InstallPicker.Visibility == Visibility.Visible && InstallPicker.ActualWidth > 0 && InstallPicker.ActualHeight > 0)
            {
                var scale = root.RasterizationScale;
                var bounds = InstallPicker.TransformToVisual(null)
                    .TransformBounds(new Windows.Foundation.Rect(0, 0, InstallPicker.ActualWidth, InstallPicker.ActualHeight));
                rect = new RectInt32(ToPixels(bounds.X, scale), ToPixels(bounds.Y, scale), ToPixels(bounds.Width, scale), ToPixels(bounds.Height, scale));
            }

            if (!force && !_passthroughStale && rect == _passthrough)
            {
                return;
            }

            _passthrough = rect;
            _passthroughStale = false;
            InputNonClientPointerSource.GetForWindowId(_windowId)
                .SetRegionRects(NonClientRegionKind.Passthrough, rect.Width == 0 || rect.Height == 0 ? [] : [rect]);
        }
        catch (Exception ex) when (ex is ArgumentException or COMException or ObjectDisposedException)
        {
            _logger.Warn(ex, "Title bar passthrough region not updated");
        }
    }

    private static int ToPixels(double value, double scale) =>
        double.IsFinite(value * scale) ? Math.Max(0, (int)Math.Round(value * scale)) : 0;

    private void SelectAllSignInUrl(object sender, RoutedEventArgs e) => SignInUrlBox.SelectAll();

    private WowInstallViewModel? TaggedInstall(object sender, string handler)
    {
        if (((FrameworkElement)sender).Tag is WowInstallViewModel install)
        {
            return install;
        }

        ViewModel.WarnUi($"{handler} could not resolve its {nameof(WowInstallViewModel)} from the element's Tag");
        return null;
    }

    private void OnInstallFlyoutOpening(object? sender, object e) => InstallPickerRunningDot.Opacity = 0;

    private void OnInstallFlyoutClosed(object? sender, object e) => InstallPickerRunningDot.Opacity = 1;

    private void OnInstallOptionClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        if (TaggedInstall(sender, nameof(OnInstallOptionClick)) is { } install)
        {
            ViewModel.SelectInstall(install);
        }
    }

    private void OnEditInstallOptionClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        if (TaggedInstall(sender, nameof(OnEditInstallOptionClick)) is { } install)
        {
            ViewModel.RequestEditInstall(install);
        }
    }

    private void OnOpenAddOnsFolderClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        ViewModel.SelectedInstall?.OpenFolderCommand.Execute(null);
    }

    private void OnAddInstallClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        ViewModel.BrowseCommand.Execute(null);
    }

    private void OnManageInstallsClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        ViewModel.OpenSettingsCommand.Execute(null);
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : ((args.SelectedItem as NavigationViewItem)?.Tag as string) switch
            {
                "sync" => typeof(SyncPage),
                "guides" => typeof(GuidesPage),
                _ => typeof(HomePage),
            };

        ViewModel.IsSettingsShown = args.IsSettingsSelected;
        ShowPage(page);
    }

    // Pages are DI singletons: Frame.Navigate would build a new one per visit, and each discarded page's repeater stays subscribed to the shared collections.
    private void ShowPage(Type pageType)
    {
        if (RootFrame.Content?.GetType() == pageType)
        {
            return;
        }

        var page = (Page)_services.GetRequiredService(pageType);
        RootFrame.Content = page;
        PlayEntrance(page);
    }

    private static void PlayEntrance(UIElement page)
    {
        var translate = new TranslateTransform { Y = 18 };
        page.RenderTransform = translate;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(220));

        var fade = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(fade, page);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(slide, translate);
        Storyboard.SetTargetProperty(slide, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SyncVisibility)
            && ViewModel.SyncVisibility == Visibility.Collapsed
            && RootFrame.Content is SyncPage)
        {
            Nav.SelectedItem = AddonsItem;
            return;
        }

        if (e.PropertyName is not nameof(MainViewModel.GuidesVisibility)
            || ViewModel.GuidesVisibility == Visibility.Visible
            || RootFrame.Content is not GuidesPage
            || (ViewModel.RestedXp.IsSessionExpired && ViewModel.HasGuidesFeature))
        {
            return;
        }

        Nav.SelectedItem = AddonsItem;
    }

    public void ShowGuidesPreview(string scenario)
    {
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));
        if (GuidesPreview.OpensDialog(scenario))
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                await ShowRestedXpSignInAsync();
            });
            return;
        }

        Nav.SelectedItem = GuidesItem;
    }

    public async Task ShowRestedXpSignInAsync()
    {
        await AppDialogs.ShowAsync(new RestedXpSignInDialog(ViewModel.RestedXp), Content.XamlRoot);

        if (ViewModel.RestedXp.IsSignedIn)
        {
            Nav.SelectedItem = GuidesItem;
        }
        else if (RootFrame.Content is GuidesPage)
        {
            Nav.SelectedItem = AddonsItem;
        }
    }

    private async Task ShowDialogAsync(ContentDialog dialog) => await AppDialogs.ShowAsync(dialog, Content.XamlRoot);

    private Task<bool> ConfirmAsync(string title, string body, string primary) => ShowMessageAsync(title, body, primary, "Cancel");

    private async Task<bool> ShowMessageAsync(string title, string body, string? primary, string close)
    {
        var dialog = new ContentDialog
        {
            Title = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap },
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary ?? "",
            CloseButtonText = close,
            DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.None,
        };
        return await AppDialogs.ShowAsync(dialog, Content.XamlRoot) == ContentDialogResult.Primary;
    }

    private async Task<int?> ChooseLinkFileAsync(string title, IReadOnlyList<string> options, int selected, string hint)
    {
        var files = new ComboBox { ItemsSource = options, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        var dialog = new ContentDialog
        {
            Title = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap },
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    files,
                    new TextBlock
                    {
                        Text = hint,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    },
                },
            },
            PrimaryButtonText = "Install",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await AppDialogs.ShowAsync(dialog, Content.XamlRoot) == ContentDialogResult.Primary && files.SelectedIndex >= 0 ? files.SelectedIndex : null;
    }

    private async Task<GuildOptionViewModel?> ChooseGuildAsync(IReadOnlyList<GuildOptionViewModel> guilds, GuildOptionViewModel preselected)
    {
        if (!AppWindow.IsVisible || Content.XamlRoot is not { } root)
        {
            return null;
        }

        var dialog = new ChooseGuildDialog(guilds, preselected);
        await AppDialogs.ShowAsync(dialog, root);
        return dialog.Selected;
    }

    private void OnGuildOptionClick(object sender, RoutedEventArgs e)
    {
        GuildFlyout.Hide();
        AccountFlyout.Hide();
        if (((FrameworkElement)sender).Tag is GuildOptionViewModel option)
        {
            ViewModel.ChooseGuildOption(option);
        }
        else
        {
            ViewModel.WarnUi($"{nameof(OnGuildOptionClick)} could not resolve its {nameof(GuildOptionViewModel)} from the element's Tag");
        }
    }

    private void OnGuildPanelClick(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        Process.Start(new ProcessStartInfo("https://guild.hoobi.io") { UseShellExecute = true })?.Dispose();
    }

    private void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        Nav.SelectedItem = AddonsItem;
        ViewModel.SignOutCommand.Execute(null);
    }

    private void OnTrayOpenClick(object sender, RoutedEventArgs e) => ShowFromTray();

    private void OnTrayRefreshClick(object sender, RoutedEventArgs e) => ViewModel.RefreshCommand.Execute(null);

    private void OnTrayQuitClick(object sender, RoutedEventArgs e) => QuitCompletely();

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_quitting)
        {
            return;
        }

        args.Cancel = true;
        if (ViewModel.CloseToTray)
        {
            MinimizeThenHideToTray();
            return;
        }

        _ = ConfirmCloseAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        if (_confirmingClose)
        {
            return;
        }

        _confirmingClose = true;
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            {
                presenter.Restore();
            }

            var dialog = new ContentDialog
            {
                Title = new TextBlock { Text = "Are you sure you want to close?", TextWrapping = TextWrapping.Wrap },
                Content = new TextBlock
                {
                    Text = "This will prevent automatic addon updates and guild syncing.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
                PrimaryButtonText = "Close anyway",
                SecondaryButtonText = "Minimize to tray",
            };
            dialog.Opened += (_, _) => dialog.FindDescendant<Button>(b => b.Name == "SecondaryButton")?.Focus(FocusState.Programmatic);
            dialog.Resources["AccentButtonBackground"] = Application.Current.Resources["CriticalButtonBackground"];
            dialog.Resources["AccentButtonBackgroundPointerOver"] = Application.Current.Resources["CriticalButtonBackgroundPointerOver"];
            dialog.Resources["AccentButtonBackgroundPressed"] = Application.Current.Resources["CriticalButtonBackgroundPressed"];
            dialog.Resources["AccentButtonForeground"] = Application.Current.Resources["CriticalButtonForeground"];
            dialog.Resources["AccentButtonForegroundPointerOver"] = Application.Current.Resources["CriticalButtonForeground"];
            dialog.Resources["AccentButtonForegroundPressed"] = Application.Current.Resources["CriticalButtonForeground"];

            switch (await AppDialogs.ShowAsync(dialog, Content.XamlRoot))
            {
                case ContentDialogResult.Primary:
                    QuitCompletely();
                    break;
                case ContentDialogResult.Secondary:
                    MinimizeThenHideToTray();
                    break;
            }
        }
        finally
        {
            _confirmingClose = false;
        }
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange
            && (ViewModel.MinimizeToTray || _hideWhenMinimized)
            && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            _hideWhenMinimized = false;
            HideToTray();
        }
    }

    private void MinimizeThenHideToTray()
    {
        if (AppWindow.IsVisible && AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } presenter)
        {
            _hideWhenMinimized = true;
            presenter.Minimize();
            return;
        }

        HideToTray();
    }

    public void HideToTray()
    {
        AppWindow.Hide();
        // SetEfficiencyMode also drops the process to Idle priority, where a synchronous message from another app (a game restoring the display on exit) waits on a starved UI thread.
        EfficiencyModeUtilities.SetProcessQualityOfServiceLevel(QualityOfServiceLevel.Low);
        _trimTimer?.Stop();
        _trimTimer = DispatcherQueue.CreateTimer();
        _trimTimer.Interval = TimeSpan.FromSeconds(3);
        _trimTimer.IsRepeating = false;
        _trimTimer.Tick += (_, _) => TrimIfHidden();
        _trimTimer.Start();
    }

    public void TrimIfHidden()
    {
        if (!AppWindow.IsVisible)
        {
            Native.TrimWorkingSet();
        }
    }

    public void ShowFromTray()
    {
        _trimTimer?.Stop();
        EfficiencyModeUtilities.SetProcessQualityOfServiceLevel(QualityOfServiceLevel.Default);
        AppWindow.Show(activateWindow: true);
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        Native.ForceForeground(ViewModel.OwnerWindowHandle);
        ViewModel.ResumeGuildPrompt();
    }

    public void ShowWithoutFocus()
    {
        _trimTimer?.Stop();
        EfficiencyModeUtilities.SetProcessQualityOfServiceLevel(QualityOfServiceLevel.Default);
        AppWindow.Show(activateWindow: false);
        // XAML renders nothing until the first Activate; on an already visible window it is only SetActiveWindow, which a background process cannot turn into foreground.
        Activate();
    }

    private void QuitCompletely()
    {
        _quitting = true;
        TrayIcon.Dispose();
        Application.Current.Exit();
    }

    // Caps the wait at 10s so a busy row can never blow the newcomer's own 15s budget for the global mutex.
    public async Task QuitFromAnotherBuildAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (ViewModel.IsAnyRowBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        QuitCompletely();
    }

    private nint OnSessionEndSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nint uIdSubclass, nint dwRefData)
    {
        if (uMsg is Native.WM_QUERYENDSESSION or Native.WM_ENDSESSION)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_quitting)
                {
                    QuitCompletely();
                }
            });
        }

        return Native.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}

internal static class Native
{
    private const int SM_CXSMICON = 49;

    public static int SmallIconSize => GetSystemMetrics(SM_CXSMICON);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSizeEx(nint hProcess, nint minimumWorkingSetSize, nint maximumWorkingSetSize, uint flags);

    public static void TrimWorkingSet() => SetProcessWorkingSetSizeEx(GetCurrentProcess(), -1, -1, 0);

    public const uint WM_QUERYENDSESSION = 0x11;
    public const uint WM_ENDSESSION = 0x16;

    public delegate nint SubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    // Windows sends these to request/confirm a logoff, reboot or app-update shutdown; unhandled, the app would not exit until Windows force-kills it.
    public static void HookSessionEnd(nint handle, SubclassProc proc) => SetWindowSubclass(handle, proc, nint.Zero, nint.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart([MarshalAs(UnmanagedType.LPWStr)] string? pwzCommandline, int dwFlags);

    // RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT: only an update should restart the app, not a crash, hang or reboot.
    private const int RestartNoCrashHangReboot = 1 | 2 | 8;

    // Windows closes the app to apply a Store update and relaunches it under this registration; neither argument lets the relaunch take focus.
    public static int RegisterRestartForStoreUpdate(nint handle) =>
        RegisterApplicationRestart(
            IsWindowVisible(handle) ? StartupRegistration.NoActivateArgument : StartupRegistration.TrayArgument,
            RestartNoCrashHangReboot);

    // Windows refuses SetForegroundWindow to a process that did not receive the last input event; a tray click goes to explorer, so borrow its input queue for the call.
    public static void ForceForeground(nint handle)
    {
        var foreground = GetForegroundWindow();
        if (foreground == handle)
        {
            return;
        }

        var owner = GetWindowThreadProcessId(foreground, nint.Zero);
        var self = GetCurrentThreadId();
        var attached = owner != self && AttachThreadInput(self, owner, true);

        BringWindowToTop(handle);
        SetForegroundWindow(handle);

        if (attached)
        {
            AttachThreadInput(self, owner, false);
        }
    }
}
