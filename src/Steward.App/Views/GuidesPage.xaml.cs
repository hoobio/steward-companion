using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class GuidesPage : Page
{
    public GuidesPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private void OnSignInClick(object sender, RoutedEventArgs e) => ViewModel?.ShowRestedXpSignIn?.Invoke();

    private void OnSessionBarClosed(InfoBar sender, object args) => ViewModel?.NavigateToAddons?.Invoke();
}
