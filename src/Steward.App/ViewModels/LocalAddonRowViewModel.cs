using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public sealed partial class LocalAddonRowViewModel : ObservableObject, IAddonTableRow
{
    private readonly WowInstall _install;
    private readonly LocalAddon _addon;
    private readonly AppStateStore _stateStore;
    private readonly Func<string, string, Task<bool>> _confirmUninstall;
    private readonly Action<LocalAddonRowViewModel> _removed;

    public LocalAddonRowViewModel(
        WowInstall install,
        LocalAddon addon,
        AppStateStore stateStore,
        string? outOfDateTip,
        Func<string, string, Task<bool>> confirmUninstall,
        Action<LocalAddonRowViewModel> removed)
    {
        _install = install;
        _addon = addon;
        _stateStore = stateStore;
        _confirmUninstall = confirmUninstall;
        _removed = removed;
        OutOfDateTip = outOfDateTip;
        InitialsBrush = InitialsTile.Brush(addon.FolderName);
        _ = LoadIconAsync();
    }

    public Brush InitialsBrush { get; }

    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    private async Task LoadIconAsync() => Icon = await LocalAddonIcon.LoadAsync(_install.AddOnsPath, _addon.FolderName).ConfigureAwait(true);

    public string DisplayName => _addon.Name;

    public string Source => "Local";

    public string HiddenId => _addon.FolderName;

    public bool IsPendingUpdate => false;

    public string Initials => InitialsTile.Text(DisplayName);

    public string FolderLine => _addon.FoldedFolders.Count == 0
        ? _addon.FolderName
        : $"{_addon.FolderName} + {_addon.FoldedFolders.Count} folder{(_addon.FoldedFolders.Count == 1 ? "" : "s")}";

    public string Version => _addon.Version ?? "";

    public string? OutOfDateTip { get; }

    public Visibility OutOfDateVisibility => When(OutOfDateTip is not null);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideLabel), nameof(HiddenPillVisibility))]
    public partial bool IsHidden { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailedVisibility))]
    public partial string? FailureMessage { get; set; }

    public string HideLabel => IsHidden ? "Show addon" : "Hide addon";

    public Visibility HiddenPillVisibility => When(IsHidden);

    public Visibility FailedVisibility => When(FailureMessage is not null);

    private string FolderPath => Path.Combine(_install.AddOnsPath, _addon.FolderName);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public bool Matches(LocalAddon addon) =>
        string.Equals(addon.FolderName, _addon.FolderName, StringComparison.OrdinalIgnoreCase)
        && addon.Name == _addon.Name
        && addon.Version == _addon.Version
        && addon.Interface == _addon.Interface
        && addon.FoldedFolders.SequenceEqual(_addon.FoldedFolders, StringComparer.OrdinalIgnoreCase);

    [RelayCommand]
    private void ToggleHidden()
    {
        var state = _stateStore.Load();
        if (IsHidden)
        {
            state.HiddenAddons.RemoveAll(id => string.Equals(id, HiddenId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            state.HiddenAddons.Add(HiddenId);
        }

        _stateStore.Save(state);
        IsHidden = !IsHidden;
    }

    [RelayCommand]
    private void OpenFolder() =>
        Process.Start(new ProcessStartInfo(Directory.Exists(FolderPath) ? FolderPath : _install.AddOnsPath) { UseShellExecute = true })?.Dispose();

    [RelayCommand]
    private async Task UninstallAsync()
    {
        var folders = _addon.FoldedFolders.Count == 0
            ? _addon.FolderName
            : $"{_addon.FolderName} and {_addon.FoldedFolders.Count} more folder{(_addon.FoldedFolders.Count == 1 ? "" : "s")}";
        if (!await _confirmUninstall(DisplayName, folders).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            foreach (var folder in _addon.FoldedFolders.Prepend(_addon.FolderName))
            {
                AddonUpdater.RemoveExistingInstall(_install.AddOnsPath, folder);
            }

            FailureMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FailureMessage = $"Uninstall failed: {ex.Message}";
        }

        _removed(this);
    }
}
