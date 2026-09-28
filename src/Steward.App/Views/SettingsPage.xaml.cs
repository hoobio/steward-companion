using System.Diagnostics;

using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _editing;

    public SettingsPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.EditInstallRequested += OnEditInstallRequested;
                _ = ShowPendingEditAsync();
            }
        };
        Unloaded += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.EditInstallRequested -= OnEditInstallRequested;
            }
        };
    }

    public MainViewModel ViewModel { get; }

    private void OnEditInstallRequested(object? sender, EventArgs e) => _ = ShowPendingEditAsync();

    private async Task ShowPendingEditAsync()
    {
        if (_editing || ViewModel?.TakeEditInstallRequest() is not { } install)
        {
            return;
        }

        _editing = true;
        try
        {
            await new EditInstallDialog(ViewModel, install) { XamlRoot = XamlRoot }.ShowAsync();
        }
        finally
        {
            _editing = false;
        }
    }

    private void OnEditInstallClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is WowInstallViewModel install)
        {
            ViewModel?.RequestEditInstall(install);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        Column.Width = Math.Min(e.NewSize.Width, Column.MaxWidth);

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
            Open(App.DisplayDataFolder(viewModel.DataFolder));
        }
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            Directory.CreateDirectory(viewModel.LogFolder);
            Open(App.DisplayDataFolder(viewModel.LogFolder));
        }
    }
}
