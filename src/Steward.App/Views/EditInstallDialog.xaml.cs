using Steward.App.ViewModels;
using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Steward.App.Views;

public sealed partial class EditInstallDialog : ContentDialog
{
    private readonly MainViewModel _main;
    private readonly WowInstallViewModel _install;
    private readonly IReadOnlyList<GameVersionOption> _gameVersions;
    private string _flavourPath;

    public EditInstallDialog(MainViewModel main, WowInstallViewModel install)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(install);

        _main = main;
        _install = install;
        _flavourPath = install.FlavourPath;
        InitializeComponent();

        _gameVersions = main.GameVersions;
        NameBox.Text = install.UserLabel ?? "";
        GameVersionBox.ItemsSource = _gameVersions;
        GameVersionBox.SelectedItem = _gameVersions.FirstOrDefault(option => option.Code == install.Install.ProductCode);
        ShowFolder();
        UpdateBusyState();
        install.RowsChanged += OnRowsChanged;
        Closed += (_, _) => install.RowsChanged -= OnRowsChanged;
    }

    private void OnRowsChanged(object? sender, EventArgs e) => UpdateBusyState();

    private void OnNameTextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        var stripped = InstallNames.StripNonAscii(NameBox.Text);
        if (stripped == NameBox.Text)
        {
            return;
        }

        var caret = Math.Max(0, NameBox.SelectionStart - (NameBox.Text.Length - stripped.Length));
        NameBox.Text = stripped;
        NameBox.SelectionStart = Math.Min(caret, stripped.Length);
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
        NameBox.PlaceholderText = _install.UserLabel is null && SelectedGameVersion?.Code == _install.Install.ProductCode
            ? _install.Label
            : SelectedGameVersion?.Name ?? "";
        GameVersionError.Visibility = Visibility.Collapsed;
        UpdateBusyState();
    }

    private bool IsBlockedByBusyRow =>
        _install.IsAnyRowBusy && MainViewModel.IsStructuralEdit(_install, SelectedGameVersion?.Code, _flavourPath);

    private void UpdateBusyState()
    {
        IsPrimaryButtonEnabled = !IsBlockedByBusyRow;
        BusyNote.Visibility = IsBlockedByBusyRow ? Visibility.Visible : Visibility.Collapsed;
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
            GameVersionBox.SelectedItem = _gameVersions.FirstOrDefault(option => option.Code == detected);
        }

        ShowFolder();
        UpdateBusyState();
    }

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SelectedGameVersion is not { } gameVersion)
        {
            args.Cancel = true;
            GameVersionError.Visibility = Visibility.Visible;
            return;
        }

        if (IsBlockedByBusyRow)
        {
            args.Cancel = true;
            UpdateBusyState();
            return;
        }

        _main.SaveInstallEdit(_install, NameBox.Text, gameVersion.Code, _flavourPath);
    }
}
