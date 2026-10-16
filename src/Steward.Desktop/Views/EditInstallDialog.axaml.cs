using Avalonia.Controls;
using Avalonia.Interactivity;

using FluentAvalonia.UI.Controls;

using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.Desktop.Views;

public sealed partial class EditInstallDialog : FAContentDialog
{
    private readonly IReadOnlyList<GameVersionOption> _gameVersions;
    private string _flavourPath;

    public EditInstallDialog(MainViewModel main, WowInstallViewModel install)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(install);

        Main = main;
        Install = install;
        _flavourPath = install.FlavourPath;
        DataContext = install;
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

    public MainViewModel Main { get; }

    public WowInstallViewModel Install { get; }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private GameVersionOption? SelectedGameVersion => GameVersionBox.SelectedItem as GameVersionOption;

    private bool IsBlockedByBusyRow =>
        Install.IsAnyRowBusy && MainViewModel.IsStructuralEdit(Install, SelectedGameVersion?.Code, _flavourPath);

    private void OnRowsChanged(object? sender, EventArgs e) => UpdateBusyState();

    private void OnNameTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = NameBox.Text ?? "";
        var stripped = InstallNames.StripNonAscii(text);
        if (stripped == text)
        {
            return;
        }

        var caret = Math.Max(0, NameBox.SelectionStart - (text.Length - stripped.Length));
        NameBox.Text = stripped;
        NameBox.SelectionStart = NameBox.SelectionEnd = Math.Min(caret, stripped.Length);
    }

    private void ShowFolder()
    {
        FolderText.Text = _flavourPath;
        ToolTip.SetTip(FolderText, _flavourPath);
        GameVersionHint.Text = Main.DetectedProduct(_flavourPath) is null
            ? "Required. Decides which addon builds Steward installs here."
            : "Detected from .flavor.info. Change it only if detection is wrong.";
    }

    private void OnGameVersionChanged(object? sender, SelectionChangedEventArgs e)
    {
        NameBox.PlaceholderText = Install.UserLabel is null && SelectedGameVersion?.Code == Install.Install.ProductCode
            ? Install.Label
            : SelectedGameVersion?.Name ?? "";
        GameVersionError.IsVisible = false;
        UpdateBusyState();
    }

    private void UpdateBusyState()
    {
        IsPrimaryButtonEnabled = !IsBlockedByBusyRow;
        BusyNote.IsVisible = IsBlockedByBusyRow;
    }

    private async void OnChangeFolderClick(object? sender, RoutedEventArgs e)
    {
        if (await Main.PickFolderAsync() is not { } path)
        {
            return;
        }

        var error = Main.ValidateInstallFolder(path, Install);
        FolderError.Text = error ?? "";
        FolderError.IsVisible = error is not null;
        if (error is not null)
        {
            return;
        }

        _flavourPath = Path.TrimEndingDirectorySeparator(path);
        if (Main.DetectedProduct(_flavourPath) is { } detected)
        {
            GameVersionBox.SelectedItem = _gameVersions.FirstOrDefault(option => option.Code == detected);
        }

        ShowFolder();
        UpdateBusyState();
    }

    private void OnSaveClick(FAContentDialog sender, FAContentDialogButtonClickEventArgs args)
    {
        if (SelectedGameVersion is not { } gameVersion)
        {
            args.Cancel = true;
            GameVersionError.IsVisible = true;
            return;
        }

        if (IsBlockedByBusyRow)
        {
            args.Cancel = true;
            UpdateBusyState();
            return;
        }

        Main.SaveInstallEdit(Install, NameBox.Text, gameVersion.Code, _flavourPath);
    }
}
