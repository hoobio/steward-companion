using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

namespace Steward.App.ViewModels;

public sealed partial class AddonRowViewModel : ObservableObject
{
    private readonly WowInstall _install;
    private readonly ManagedAddon _addon;
    private readonly AddonUpdater _updater;
    private readonly AppStateStore _stateStore;
    private readonly Func<CancellationToken, Task<bool>> _ensureAuthorized;

    private AddonRelease? _latestRelease;

    public AddonRowViewModel(
        WowInstall install,
        ManagedAddon addon,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized)
    {
        _install = install;
        _addon = addon;
        _updater = updater;
        _stateStore = stateStore;
        _ensureAuthorized = ensureAuthorized;
        Channel = stateStore.Load().Channels.GetValueOrDefault(addon.Id) ?? AppStateStore.DefaultChannel;
        RefreshInstalledVersion();
    }

    public string AddonId => _addon.Id;

    public string FolderName => _addon.FolderName;

    [ObservableProperty]
    public partial string Channel { get; set; }

    [ObservableProperty]
    public partial string? InstalledVersion { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyPropertyChangedFor(nameof(UpdateLabel))]
    public partial string? AvailableVersion { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial bool IsAdmin { get; set; }

    [ObservableProperty]
    public partial double UpdateProgress { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public string UpdateLabel => AvailableVersion is null ? "Update" : $"Update to {AvailableVersion}";

    public void RefreshInstalledVersion()
    {
        var state = _stateStore.Load();
        var record = state.Installs.GetValueOrDefault(AppStateStore.Key(_install.FlavourPath, _addon.Id));
        InstalledVersion = record?.Version
            ?? TocFile.ReadVersion(Path.Combine(_install.AddOnsPath, _addon.FolderName, $"{_addon.FolderName}.toc"));
    }

    public async Task RefreshAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            _latestRelease = await _updater.GetLatestAsync(_addon, Channel, cancellationToken).ConfigureAwait(false);
            AvailableVersion = _latestRelease?.Version;
            StatusMessage = _latestRelease is null ? $"Nothing released on {Channel} yet." : null;
        }
        catch (Exception ex)
        {
            _latestRelease = null;
            AvailableVersion = null;
            StatusMessage = ex.Message;
        }
    }

    private bool CanUpdate =>
        IsAdmin &&
        _latestRelease is not null &&
        TocFile.HasUpdate(_latestRelease.Version, InstalledVersion);

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        if (_latestRelease is null)
        {
            return;
        }

        if (!await _ensureAuthorized(CancellationToken.None).ConfigureAwait(true))
        {
            StatusMessage = "Not authorised to update.";
            return;
        }

        IsBusy = true;
        UpdateProgress = 0;
        try
        {
            var progress = new Progress<double>(value => UpdateProgress = value);
            await _updater.InstallAsync(_addon, Channel, _latestRelease, _install.AddOnsPath, progress, CancellationToken.None)
                .ConfigureAwait(true);

            var state = _stateStore.Load();
            state.Installs[AppStateStore.Key(_install.FlavourPath, _addon.Id)] =
                new InstalledAddonRecord(_latestRelease.Version, Channel, _latestRelease.Sha256);
            _stateStore.Save(state);

            InstalledVersion = _latestRelease.Version;
            StatusMessage = $"Updated to {_latestRelease.Version}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
