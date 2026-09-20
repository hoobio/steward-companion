using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Steward.App.Views;

public sealed partial class GuidesPage : Page
{
    public GuidesPage()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = e.Parameter as MainViewModel;
        Bindings.Update();
    }

    private void OnSignInClick(object sender, RoutedEventArgs e) => ViewModel?.ShowRestedXpSignIn?.Invoke();

    private void OnSessionBarClosed(InfoBar sender, object args) => ViewModel?.NavigateToAddons?.Invoke();
}
