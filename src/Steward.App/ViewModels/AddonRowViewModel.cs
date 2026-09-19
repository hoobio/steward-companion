using System.Diagnostics;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.ApplicationModel.DataTransfer;

namespace Steward.App.ViewModels;

public enum AddonRowState
{
    NoReleases,
    Missing,
    UpdateAvailable,
    Current,
    Updating,
    Failed,
}

public sealed partial class AddonRowViewModel : ObservableObject
{
    private static readonly string[] DerivedNames =
    [
        nameof(State),
        nameof(HasUpdateAvailable),
        nameof(IsInstalled),
        nameof(HasNoReleases),
        nameof(ActionLabel),
        nameof(ActionStyle),
        nameof(UpdatingLine),
        nameof(VersionPairVisibility),
        nameof(SingleVersionVisibility),
        nameof(NotInstalledVisibility),
        nameof(NoReleasesVisibility),
        nameof(UpdatingVisibility),
        nameof(FailedVisibility),
        nameof(ChannelPillVisibility),
        nameof(ActionVisibility),
        nameof(UpToDateVisibility),
        nameof(MemberPillVisibility),
        nameof(NoticeVisibility),
        nameof(CanAutoApply),
    ];

    private readonly WowInstall _install;
    private readonly ManagedAddon _addon;
    private readonly AddonUpdater _updater;
    private readonly AppStateStore _stateStore;
    private readonly Func<CancellationToken, Task<bool>> _ensureAuthorized;
    private readonly Action _changeChannelRequested;

    private AddonChannelStatus? _status;

    public ImageSource Icon { get; }

    public AddonRowViewModel(
        WowInstall install,
        ManagedAddon addon,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized,
        Action changeChannelRequested)
    {
        _install = install;
        _addon = addon;
        _updater = updater;
        _stateStore = stateStore;
        _ensureAuthorized = ensureAuthorized;
        _changeChannelRequested = changeChannelRequested;
        Icon = new BitmapImage(new Uri(new Uri(addon.ManifestBaseUrl), "icon.png"));
        RefreshInstalledVersion();
    }

    public string AddonId => _addon.Id;

    public string FolderName => _addon.FolderName;

    public string DisplayName => _addon.FolderName;

    public bool IsFirst { get; init; }

    public Thickness HairlineThickness => IsFirst ? default : new Thickness(0, 1, 0, 0);

    [ObservableProperty]
    public partial string? Channel { get; set; }

    [ObservableProperty]
    public partial string? InstalledVersion { get; set; }

    [ObservableProperty]
    public partial string? AvailableVersion { get; set; }

    [ObservableProperty]
    public partial bool IsAdmin { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double UpdateProgress { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasFailed { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReloadHintVisibility))]
    public partial bool NeedsReload { get; set; }

    public bool IsClientRunning { get; set; }

    public Visibility ReloadHintVisibility => When(NeedsReload);

    public bool CanAutoApply => HasUpdateAvailable && !IsBusy && !HasFailed && IsAdmin && !IsClientRunning;

    public bool HasUpdateAvailable =>
        _status?.Release is { } release && Channel is not null && TocFile.HasUpdate(release.Version, InstalledVersion);

    public bool IsInstalled => InstalledVersion is not null;

    public bool HasNoReleases => _status is { Channel: null };

    public AddonRowState State => true switch
    {
        _ when IsBusy => AddonRowState.Updating,
        _ when HasFailed => AddonRowState.Failed,
        _ when HasNoReleases => AddonRowState.NoReleases,
        _ when !IsInstalled => AddonRowState.Missing,
        _ when HasUpdateAvailable => AddonRowState.UpdateAvailable,
        _ => AddonRowState.Current,
    };

    public string ActionLabel => true switch
    {
        _ when IsBusy => "Updating",
        _ when State == AddonRowState.Missing => "Install",
        _ when RecordedChannelDiffers && AvailableVersion is { } version => $"Switch to {version}",
        _ => "Update",
    };

    public Style ActionStyle => (Style)Application.Current.Resources[
        State == AddonRowState.Missing ? "DefaultButtonStyle" : "AccentButtonStyle"];

    public string ProgressText => UpdateProgress.ToString("P0", CultureInfo.CurrentCulture);

    public string UpdatingLine => $"Installing {_status?.Release?.Version}, verifying download";

    public Visibility VersionPairVisibility => When(State == AddonRowState.UpdateAvailable);

    public Visibility SingleVersionVisibility => When(State == AddonRowState.Current);

    public Visibility NotInstalledVisibility => When(State == AddonRowState.Missing);

    public Visibility NoReleasesVisibility => When(State == AddonRowState.NoReleases);

    public Visibility UpdatingVisibility => When(State == AddonRowState.Updating);

    public Visibility FailedVisibility => When(State == AddonRowState.Failed);

    public Visibility ChannelPillVisibility => When(Channel is not null);

    public Visibility ActionVisibility =>
        When(IsAdmin && State is AddonRowState.UpdateAvailable or AddonRowState.Missing);

    public Visibility UpToDateVisibility => When(State == AddonRowState.Current);

    public Visibility MemberPillVisibility => When(!IsAdmin && State == AddonRowState.UpdateAvailable);

    public Visibility NoticeVisibility =>
        When(State != AddonRowState.Failed && !string.IsNullOrEmpty(StatusMessage));

    private bool RecordedChannelDiffers =>
        Record is { } record && Channel is not null && !string.Equals(record.Channel, Channel, StringComparison.OrdinalIgnoreCase);

    private InstalledAddonRecord? Record =>
        _stateStore.Load().Installs.GetValueOrDefault(AppStateStore.Key(_install.FlavourPath, _addon.Id));

    private string AddonFolderPath => Path.Combine(_install.AddOnsPath, _addon.FolderName);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshInstalledVersion()
    {
        var state = _stateStore.Load();
        var record = state.Installs.GetValueOrDefault(AppStateStore.Key(_install.FlavourPath, _addon.Id));
        InstalledVersion = record?.Version
            ?? TocFile.ReadVersion(Path.Combine(_install.AddOnsPath, _addon.FolderName, $"{_addon.FolderName}.toc"));
    }

    public void Apply(AddonChannelStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var previousAvailable = AvailableVersion;
        _status = status;
        Channel = status.Channel;
        AvailableVersion = status.Release?.Version;
        if (!string.Equals(previousAvailable, AvailableVersion, StringComparison.Ordinal))
        {
            HasFailed = false;
        }

        StatusMessage = status.Channel is null ? null : status.Notice;
        NotifyDerived();
    }

    partial void OnChannelChanged(string? value) => NotifyDerived();

    partial void OnInstalledVersionChanged(string? value) => NotifyDerived();

    partial void OnAvailableVersionChanged(string? value) => NotifyDerived();

    partial void OnIsAdminChanged(bool value) => NotifyDerived();

    partial void OnIsBusyChanged(bool value) => NotifyDerived();

    partial void OnHasFailedChanged(bool value) => NotifyDerived();

    partial void OnStatusMessageChanged(string? value) => NotifyDerived();

    private void NotifyDerived()
    {
        foreach (var name in DerivedNames)
        {
            OnPropertyChanged(name);
        }

        UpdateCommand.NotifyCanExecuteChanged();
        CopySha256Command.NotifyCanExecuteChanged();
    }

    private bool CanUpdate => IsAdmin && HasUpdateAvailable;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private Task UpdateAsync() => RunInstallAsync();

    [RelayCommand]
    private Task ReinstallAsync() => RunInstallAsync();

    private async Task RunInstallAsync()
    {
        var release = _status?.Release;
        var channel = Channel;
        if (release is null || channel is null)
        {
            return;
        }

        if (!await _ensureAuthorized(CancellationToken.None).ConfigureAwait(true))
        {
            HasFailed = true;
            StatusMessage = "Not authorised to update.";
            return;
        }

        HasFailed = false;
        StatusMessage = null;
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
            NeedsReload = IsClientRunning;
        }
        catch (Exception ex)
        {
            HasFailed = true;
            StatusMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = Directory.Exists(AddonFolderPath) ? AddonFolderPath : _install.AddOnsPath;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    private bool CanCopySha256 => Record is not null;

    [RelayCommand(CanExecute = nameof(CanCopySha256))]
    private void CopySha256()
    {
        if (Record is not { } record)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(record.Sha256);
        Clipboard.SetContent(package);
    }

    [RelayCommand]
    private void ChangeChannel() => _changeChannelRequested();
}
