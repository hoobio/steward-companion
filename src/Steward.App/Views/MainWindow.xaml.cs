using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using H.NotifyIcon.EfficiencyMode;

using Steward.App.Services;
using Steward.App.ViewModels;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.Views;

public sealed partial class MainWindow : Window
{
    private bool _quitting;

    public MainWindow(MainViewModel viewModel)
    {
        ShowWindowCommand = new RelayCommand(ShowFromTray);
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(App.IconPath);
        ViewModel = viewModel;
        Title = MainViewModel.WindowTitle;
        AppTitleBar.Title = MainViewModel.WindowTitle;
        TrayIcon.ToolTipText = MainViewModel.WindowTitle;
        ViewModel.OwnerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ViewModel.NavigateToSettings = () => Nav.SelectedItem = Nav.SettingsItem;
        ViewModel.NavigateToAddons = () => Nav.SelectedItem = AddonsItem;
        ViewModel.ShowRestedXpSignIn = () => _ = ShowRestedXpSignInAsync();
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

        // An unpackaged app cannot resolve ms-appx:/// for the tray icon, so it loads from disk.
        TrayIcon.IconSource = new BitmapImage(new Uri(App.IconPath));
        TrayIcon.ForceCreate();
        AppWindow.Closing += OnWindowClosing;
        AppWindow.Changed += OnWindowChanged;
    }

    public MainViewModel ViewModel { get; }

    public ICommand ShowWindowCommand { get; }

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

        if (RootFrame.CurrentSourcePageType == page)
        {
            return;
        }

        RootFrame.Navigate(page, ViewModel);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(MainViewModel.GuidesVisibility)
            || ViewModel.GuidesVisibility == Visibility.Visible
            || RootFrame.CurrentSourcePageType != typeof(GuidesPage)
            || ViewModel.RestedXp.IsSessionExpired)
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
        var dialog = new RestedXpSignInDialog(ViewModel.RestedXp) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();

        if (ViewModel.RestedXp.IsSignedIn)
        {
            Nav.SelectedItem = GuidesItem;
        }
        else if (RootFrame.CurrentSourcePageType == typeof(GuidesPage))
        {
            Nav.SelectedItem = AddonsItem;
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

        if (ViewModel.CloseToTray)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }

        QuitCompletely();
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange
            && ViewModel.MinimizeToTray
            && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            HideToTray();
        }
    }

    public void HideToTray()
    {
        AppWindow.Hide();
        EfficiencyModeUtilities.SetEfficiencyMode(true);
    }

    public void ShowFromTray()
    {
        EfficiencyModeUtilities.SetEfficiencyMode(false);
        AppWindow.Show(activateWindow: true);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
        }

        Activate();
        Native.ForceForeground(ViewModel.OwnerWindowHandle);
    }

    private void QuitCompletely()
    {
        _quitting = true;
        TrayIcon.Dispose();
        Application.Current.Exit();
    }
}

internal static class Native
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

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

        SetForegroundWindow(handle);

        if (attached)
        {
            AttachThreadInput(self, owner, false);
        }
    }
}
