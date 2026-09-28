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

    private static readonly (int Column, double Width, double MinTableWidth)[] CollapsibleColumns =
    [
        (3, 104, 700),
        (4, 64, 780),
    ];

    private void OnTableRowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var row = (Grid)sender;
        foreach (var (column, width, minTableWidth) in CollapsibleColumns)
        {
            row.ColumnDefinitions[column].Width = new GridLength(e.NewSize.Width >= minTableWidth ? width : 0);
        }
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
