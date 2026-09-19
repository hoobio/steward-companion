using System.Diagnostics;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using H.NotifyIcon.EfficiencyMode;

using Steward.App.ViewModels;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        SystemBackdrop = new MicaBackdrop();
        ViewModel.OwnerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ViewModel.NavigateToSettings = () => Nav.SelectedItem = Nav.SettingsItem;
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
                _ => typeof(HomePage),
            };

        if (RootFrame.CurrentSourcePageType == page)
        {
            return;
        }

        RootFrame.Navigate(page, ViewModel);
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

        if (ViewModel.KeepInTray)
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
            && ViewModel.KeepInTray
            && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        AppWindow.Hide();
        EfficiencyModeUtilities.SetEfficiencyMode(true);
    }

    private void ShowFromTray()
    {
        EfficiencyModeUtilities.SetEfficiencyMode(false);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
        }

        AppWindow.Show();
        Activate();
    }

    private void QuitCompletely()
    {
        _quitting = true;
        TrayIcon.Dispose();
        Application.Current.Exit();
    }
}
