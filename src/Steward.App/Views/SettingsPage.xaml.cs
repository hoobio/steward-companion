using System.Diagnostics;

using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Steward.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
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

    private static void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    private void OnGuildPanelClick(object sender, RoutedEventArgs e) => Open("https://guild.hoobi.io");

    private void OnReleasesClick(object sender, RoutedEventArgs e) =>
        Open("https://github.com/hoobio/steward-companion/releases");

    private void OnRepositoryClick(object sender, RoutedEventArgs e) =>
        Open("https://github.com/hoobio/steward-companion");

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            Directory.CreateDirectory(viewModel.DataFolder);
            Open(viewModel.DataFolder);
        }
    }
}
