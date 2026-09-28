using Steward.App.Services;
using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class SyncPage : Page
{
    public SyncPage(MainViewModel main)
    {
        ViewModel = main.Sync;
        InitializeComponent();
        FlyoutOpener.Attach(GuildPicker, GuildFlyout, "guild-switcher");
    }

    public SyncViewModel ViewModel { get; }

    private void GuildOptionClick(object sender, RoutedEventArgs e)
    {
        GuildFlyout.Hide();
        if (((FrameworkElement)sender).DataContext is GuildOptionViewModel option && ViewModel is not null)
        {
            ViewModel.Main.SelectedGuild = option;
        }
    }
}
