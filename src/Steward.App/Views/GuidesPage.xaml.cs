using Steward.App.ViewModels;
using Steward.Core;

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

    public static Visibility ReloadNoticeVisibility(RestedXpInstallViewModel? guide, WowClientProcess? client, DateTimeOffset? writtenAt) =>
        BindConvert.Visible(guide?.ReloadNoticeVisibility(client, writtenAt) == true);

    private void OnSignInClick(object sender, RoutedEventArgs e) => ViewModel?.ShowRestedXpSignIn?.Invoke();

    private void OnSessionBarClosed(InfoBar sender, object args) => ViewModel?.NavigateToAddons?.Invoke();
}
