using Steward.App.ViewModels;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(App.IconPath);
        ViewModel = viewModel;
        SystemBackdrop = new MicaBackdrop();
        ViewModel.OwnerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        RootFrame.Navigate(typeof(HomePage), ViewModel);

        var scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(720 * scale);
            presenter.PreferredMinimumHeight = (int)(560 * scale);
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(980 * scale), (int)(720 * scale)));
    }

    public MainViewModel ViewModel { get; }

    private void OnBackRequested(TitleBar sender, object args) => ViewModel.IsOnSettings = false;

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ViewModel.IsOnSettings = true;
}
