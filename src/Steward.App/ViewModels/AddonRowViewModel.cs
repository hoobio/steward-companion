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

    private AddonChannelStatus? _status;

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
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial string? Channel { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
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

    public void Apply(AddonChannelStatus status)
    {
        _status = status;
        Channel = status.Channel;
        AvailableVersion = status.Release?.Version;
        StatusMessage = status.Channel is null ? "No releases yet" : status.Notice;
    }

    private bool CanUpdate =>
        IsAdmin &&
        _status?.Release is not null &&
        Channel is not null &&
        TocFile.HasUpdate(_status.Release.Version, InstalledVersion);

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        var release = _status?.Release;
        var channel = Channel;
        if (release is null || channel is null)
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
            await _updater.InstallAsync(_addon, channel, release, _install.AddOnsPath, progress, CancellationToken.None)
                .ConfigureAwait(true);

            var state = _stateStore.Load();
            state.Installs[AppStateStore.Key(_install.FlavourPath, _addon.Id)] =
                new InstalledAddonRecord(release.Version, channel, release.Sha256);
            _stateStore.Save(state);

            InstalledVersion = release.Version;
            StatusMessage = $"Updated to {release.Version}.";
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
