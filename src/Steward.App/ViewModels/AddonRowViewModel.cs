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
    private string _channel = "beta";

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
        RefreshInstalledVersion();
    }

    public string AddonId => _addon.Id;

    public string FolderName => _addon.FolderName;

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

    public async Task RefreshAvailableAsync(string channel, CancellationToken cancellationToken)
    {
        _channel = channel;
        try
        {
            _latestRelease = await _updater.GetLatestAsync(_addon, channel, cancellationToken).ConfigureAwait(false);
            AvailableVersion = _latestRelease?.Version;
            StatusMessage = _latestRelease is null ? $"Nothing released on {channel} yet." : null;
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
            await _updater.InstallAsync(_addon, _channel, _latestRelease, _install.AddOnsPath, progress, CancellationToken.None)
                .ConfigureAwait(true);

            var state = _stateStore.Load();
            state.Installs[AppStateStore.Key(_install.FlavourPath, _addon.Id)] =
                new InstalledAddonRecord(_latestRelease.Version, _channel, _latestRelease.Sha256);
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
