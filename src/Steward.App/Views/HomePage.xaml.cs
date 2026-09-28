using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Steward.App.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
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

    private static readonly (int Column, double Stars, double MinWidth, double MinTableWidth)[] CollapsibleColumns =
    [
        (3, 1.2, 84, 760),
        (4, 1, 60, 840),
    ];

    private void OnTableRowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var row = (Grid)sender;
        foreach (var (column, stars, minWidth, minTableWidth) in CollapsibleColumns)
        {
            var shown = e.NewSize.Width >= minTableWidth;
            row.ColumnDefinitions[column].MinWidth = shown ? minWidth : 0;
            row.ColumnDefinitions[column].Width = shown ? new GridLength(stars, GridUnitType.Star) : new GridLength(0);
        }
    }

    private async void OnChangelogClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not AddonRowViewModel row)
        {
            return;
        }

        var notes = new StackPanel { Spacing = 6 };
        foreach (var note in row.Notes)
        {
            notes.Children.Add(new TextBlock { Text = $"• {note}", TextWrapping = TextWrapping.Wrap });
        }

        await new ContentDialog
        {
            Title = row.ChangelogTitle,
            Content = new ScrollViewer { Content = notes },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            CornerRadius = new CornerRadius(8),
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    private void OnManifestIconFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is AddonRowViewModel row)
        {
            row.ManifestIconFailed = true;
        }
    }

    private void OnInstallOptionClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        if (((FrameworkElement)sender).DataContext is WowInstallViewModel install)
        {
            ViewModel?.SelectInstall(install);
        }
    }

    private void OnEditInstallOptionClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        if (((FrameworkElement)sender).DataContext is WowInstallViewModel install)
        {
            ViewModel?.RequestEditInstall(install);
        }
    }

    private void OnAddInstallClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        ViewModel?.BrowseCommand.Execute(null);
    }

    private void OnUpdateAllClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.UpdateAllCommand.CanExecute(null) == true)
        {
            ViewModel.UpdateAllCommand.Execute(null);
        }
        else
        {
            UpdateAllMore.Flyout.ShowAt(UpdateAllMore);
        }
    }

    private void OnManageInstallsClick(object sender, RoutedEventArgs e)
    {
        InstallFlyout.Hide();
        ViewModel?.OpenSettingsCommand.Execute(null);
    }
}
