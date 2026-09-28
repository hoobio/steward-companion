using Steward.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class EditInstallDialog : ContentDialog
{
    private readonly MainViewModel _main;
    private readonly WowInstallViewModel _install;
    private string _flavourPath;

    public EditInstallDialog(MainViewModel main, WowInstallViewModel install)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(install);

        _main = main;
        _install = install;
        _flavourPath = install.FlavourPath;
        InitializeComponent();

        NameBox.Text = install.UserLabel ?? "";
        GameVersionBox.ItemsSource = main.GameVersions;
        GameVersionBox.SelectedItem = main.GameVersions.FirstOrDefault(option => option.Code == install.Install.ProductCode);
        ShowFolder();
    }

    private GameVersionOption? SelectedGameVersion => GameVersionBox.SelectedItem as GameVersionOption;

    private void ShowFolder()
    {
        FolderText.Text = _flavourPath;
        ToolTipService.SetToolTip(FolderText, _flavourPath);
        GameVersionHint.Text = _main.DetectedProduct(_flavourPath) is null
            ? "Required. Decides which addon builds Steward installs here."
            : "Detected from .flavor.info. Change it only if detection is wrong.";
    }

    private void OnGameVersionChanged(object sender, SelectionChangedEventArgs e)
    {
        NameBox.PlaceholderText = SelectedGameVersion?.Name ?? "";
        GameVersionError.Visibility = Visibility.Collapsed;
    }

    private async void OnChangeFolderClick(object sender, RoutedEventArgs e)
    {
        if (await _main.PickFolderAsync() is not { } path)
        {
            return;
        }

        var error = _main.ValidateInstallFolder(path, _install);
        FolderError.Text = error ?? "";
        FolderError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is not null)
        {
            return;
        }

        _flavourPath = Path.TrimEndingDirectorySeparator(path);
        if (_main.DetectedProduct(_flavourPath) is { } detected)
        {
            GameVersionBox.SelectedItem = _main.GameVersions.FirstOrDefault(option => option.Code == detected);
        }

        ShowFolder();
    }

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SelectedGameVersion is not { } gameVersion)
        {
            args.Cancel = true;
            GameVersionError.Visibility = Visibility.Visible;
            return;
        }

        _main.SaveInstallEdit(_install, NameBox.Text, gameVersion.Code, _flavourPath);
    }
}
