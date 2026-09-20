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
        nameof(SubtitleText),
        nameof(VersionPairVisibility),
        nameof(NoReleasesVisibility),
        nameof(UpdatingVisibility),
        nameof(FailedVisibility),
        nameof(ChannelPillVisibility),
        nameof(ActionVisibility),
        nameof(StatusGlyphVisibility),
        nameof(StatusGlyph),
        nameof(StatusGlyphBrush),
        nameof(StatusGlyphTooltip),
        nameof(MemberPillVisibility),
        nameof(NoticeVisibility),
        nameof(OverflowVisibility),
        nameof(CanAutoApply),
        nameof(HideLabel),
        nameof(RowVisibility),
        nameof(HiddenPillVisibility),
        nameof(RestedXpSignInVisibility),
    ];

    private readonly WowInstall _install;
    private readonly ManagedAddon _addon;
    private readonly AddonUpdater _updater;
    private readonly AppStateStore _stateStore;
    private readonly Func<CancellationToken, Task<bool>> _ensureAuthorized;
    private readonly Action _changeChannelRequested;
    private readonly Func<WowInstall, Task> _afterStewardInstalled;

    private AddonChannelStatus? _status;

    public ImageSource Icon { get; }

    public AddonRowViewModel(
        WowInstall install,
        ManagedAddon addon,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized,
        Action changeChannelRequested,
        Func<WowInstall, Task> afterStewardInstalled)
    {
        _install = install;
        _addon = addon;
        _updater = updater;
        _stateStore = stateStore;
        _ensureAuthorized = ensureAuthorized;
        _changeChannelRequested = changeChannelRequested;
        _afterStewardInstalled = afterStewardInstalled;
        Icon = new BitmapImage(addon.IconUri);
        IsHidden = stateStore.Load().HiddenAddons.Contains(addon.Id, StringComparer.OrdinalIgnoreCase);
        RefreshInstalledVersion();
    }

    public string AddonId => _addon.Id;

    public string DisplayName => _addon.DisplayName;

    public string SubtitleText => InstalledVersion ?? "Not installed";

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

    [ObservableProperty]
    public partial bool IsHidden { get; set; }

    [ObservableProperty]
    public partial bool ShowHidden { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestedXpSignInVisibility))]
    public partial bool NeedsRestedXpSignIn { get; set; }

    public Action? RestedXpSignInRequested { get; set; }

    public Visibility RestedXpSignInVisibility => When(NeedsRestedXpSignIn && IsInstalled && !IsHidden);

    public bool IsClientRunning { get; set; }

    public DateTimeOffset ReloadPendingSince { get; private set; }

    public Visibility ReloadHintVisibility => When(NeedsReload);

    public bool CanAutoApply => !IsHidden && IsAdmin && (State == AddonRowState.UpdateAvailable || (State == AddonRowState.Missing && _addon.AutoInstall));

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

    public Visibility NoReleasesVisibility => When(State == AddonRowState.NoReleases);

    public Visibility UpdatingVisibility => When(State == AddonRowState.Updating);

    public Visibility FailedVisibility => When(State == AddonRowState.Failed);

    public Visibility ChannelPillVisibility => When(Channel is not null);

    public Visibility ActionVisibility =>
        When(!IsHidden && IsAdmin && State is AddonRowState.UpdateAvailable or AddonRowState.Missing);

    public Visibility StatusGlyphVisibility => When(State is AddonRowState.Current or AddonRowState.UpdateAvailable);

    public string StatusGlyph => State == AddonRowState.UpdateAvailable ? "" : "";

    public Brush StatusGlyphBrush => (Brush)Application.Current.Resources[
        State == AddonRowState.UpdateAvailable ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush"];

    public string StatusGlyphTooltip => State == AddonRowState.UpdateAvailable
        ? $"{AvailableVersion} available"
        : Record?.InstalledAt is { } at ? $"Updated {RelativeTime.Describe(at, DateTimeOffset.Now)}" : "Up to date";

    public Visibility MemberPillVisibility => When(!IsHidden && !IsAdmin && State == AddonRowState.UpdateAvailable);

    public Visibility OverflowVisibility => When(State != AddonRowState.NoReleases);

    public Visibility NoticeVisibility =>
        When(State != AddonRowState.Failed && !string.IsNullOrEmpty(StatusMessage));

    public bool CanHide => !_addon.AutoInstall;

    public Visibility HideVisibility => When(CanHide);

    public string HideLabel => IsHidden ? "Show addon" : "Hide addon";

    public Visibility RowVisibility => When(!IsHidden || ShowHidden);

    public Visibility HiddenPillVisibility => When(IsHidden);

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

    partial void OnIsHiddenChanged(bool value) => NotifyDerived();

    partial void OnShowHiddenChanged(bool value) => NotifyDerived();

    private void NotifyDerived()
    {
        foreach (var name in DerivedNames)
        {
            OnPropertyChanged(name);
        }

        UpdateCommand.NotifyCanExecuteChanged();
        CopySha256Command.NotifyCanExecuteChanged();
    }

    private bool CanUpdate => !IsHidden && IsAdmin && HasUpdateAvailable;

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

        HasFailed = false;
        StatusMessage = null;
        IsBusy = true;
        UpdateProgress = 0;
        try
        {
            if (!await _ensureAuthorized(CancellationToken.None).ConfigureAwait(true))
            {
                HasFailed = true;
                StatusMessage = "Not authorised to update.";
                return;
            }

            var progress = new Progress<double>(value => UpdateProgress = value);
            await _updater.InstallAsync(_addon, channel, release, _install.AddOnsPath, progress, CancellationToken.None)
                .ConfigureAwait(true);

            var state = _stateStore.Load();
            state.Installs[AppStateStore.Key(_install.FlavourPath, _addon.Id)] =
                new InstalledAddonRecord(release.Version, channel, release.Sha256, DateTimeOffset.Now);
            _stateStore.Save(state);

            InstalledVersion = release.Version;
            ReloadPendingSince = DateTimeOffset.Now;
            NeedsReload = IsClientRunning;

            if (string.Equals(AddonId, StewardSavedVariables.AddonName, StringComparison.OrdinalIgnoreCase))
            {
                await _afterStewardInstalled(_install).ConfigureAwait(true);
            }
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

    [RelayCommand]
    private void RestedXpSignIn() => RestedXpSignInRequested?.Invoke();

    [RelayCommand]
    private void ToggleHidden()
    {
        var state = _stateStore.Load();
        if (IsHidden)
        {
            state.HiddenAddons.RemoveAll(id => string.Equals(id, AddonId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            state.HiddenAddons.Add(AddonId);
        }

        _stateStore.Save(state);
        IsHidden = !IsHidden;
    }
}
