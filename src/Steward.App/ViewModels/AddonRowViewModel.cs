using System.Diagnostics;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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

public enum AddonRowStatus
{
    UpdateAvailable,
    Updating,
    Switch,
    Failed,
    NotInstalled,
    UpToDate,
    NoReleases,
    Ignored,
    Hidden,
    Local,
}

public sealed partial class AddonRowViewModel : ObservableObject, IAddonTableRow
{
    private static readonly string[] DerivedNames =
    [
        nameof(State),
        nameof(HasUpdateAvailable),
        nameof(IsInstalled),
        nameof(HasNoReleases),
        nameof(IsPendingUpdate),
        nameof(ActionLabel),
        nameof(ActionStyle),
        nameof(UpdatingLine),
        nameof(InstalledRunText),
        nameof(NotInstalledRunText),
        nameof(VersionPairTip),
        nameof(InstalledVersionShort),
        nameof(AvailableVersionShort),
        nameof(ReleasedText),
        nameof(NewVersionBrush),
        nameof(VersionPairVisibility),
        nameof(WideVersionPairVisibility),
        nameof(StackedVersionPairVisibility),
        nameof(ReleasedVisibility),
        nameof(CurrentVersionVisibility),
        nameof(NoReleasesVisibility),
        nameof(UpdatingVisibility),
        nameof(FailedVisibility),
        nameof(ChannelChipVisibility),
        nameof(SingleChannelVisibility),
        nameof(SingleChannelTip),
        nameof(NoChannelVisibility),
        nameof(ActionVisibility),
        nameof(UpToDateVisibility),
        nameof(IgnoredPillVisibility),
        nameof(Status),
        nameof(StatusRank),
        nameof(StatusText),
        nameof(StatusBrush),
        nameof(StatusTextVisibility),
        nameof(Changelog),
        nameof(ChangelogVisibility),
        nameof(ChangelogTitle),
        nameof(VersionCellTip),
        nameof(HasVersionCellTip),
        nameof(CompactChannelChipVisibility),
        nameof(CompactSingleChannelVisibility),
        nameof(NoticeVisibility),
        nameof(OverflowVisibility),
        nameof(IgnoreVisibility),
        nameof(IgnoreLabel),
        nameof(ReleaseActionsVisibility),
        nameof(InstalledActionsVisibility),
        nameof(UninstallVisibility),
        nameof(UninstallErrorVisibility),
        nameof(OutOfDateVisibility),
        nameof(CanAutoApply),
        nameof(HideLabel),
        nameof(HiddenPillVisibility),
        nameof(RestedXpSignInVisibility),
        nameof(FolderLine),
        nameof(IsDistributable),
    ];

    private readonly WowInstall _install;
    private readonly ManagedAddon _addon;
    private readonly AddonUpdater _updater;
    private readonly AppStateStore _stateStore;
    private readonly Func<CancellationToken, Task<bool>> _ensureAuthorized;
    private readonly Action<string> _changeChannelRequested;
    private readonly Func<string, string, Task<bool>> _confirmUninstall;
    private readonly Func<string, string?> _outOfDateTip;
    private readonly Func<WowInstall, Task> _afterStewardInstalled;
    private readonly ILogger _logger;

    private AddonChannelStatus? _status;
    private int _lastUpdatedGeneration;

    public ImageSource Icon { get; }

    public AddonRowViewModel(
        WowInstall install,
        ManagedAddon addon,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized,
        Action<string> changeChannelRequested,
        Func<string, string, Task<bool>> confirmUninstall,
        Func<string, string?> outOfDateTip,
        Func<WowInstall, Task> afterStewardInstalled,
        ILogger logger)
    {
        _logger = logger;
        _install = install;
        _addon = addon;
        _updater = updater;
        _stateStore = stateStore;
        _ensureAuthorized = ensureAuthorized;
        _changeChannelRequested = changeChannelRequested;
        _confirmUninstall = confirmUninstall;
        _outOfDateTip = outOfDateTip;
        _afterStewardInstalled = afterStewardInstalled;
        Icon = ManifestIcon.For(addon);
        InitialsBrush = InitialsTile.Brush(addon.FolderName);
        var state = stateStore.Load();
        IsHidden = state.HiddenAddons.Contains(addon.Id, StringComparer.OrdinalIgnoreCase);
        IsIgnored = state.IgnoredAddons.Contains(Key, StringComparer.OrdinalIgnoreCase);
        RefreshInstalledVersion();
    }

    public string Initials => InitialsTile.Text(DisplayName);

    public Brush InitialsBrush { get; }

    [ObservableProperty]
    public partial ImageSource? FallbackIcon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManifestIconVisibility))]
    public partial bool ManifestIconFailed { get; set; }

    public Visibility ManifestIconVisibility => When(!ManifestIconFailed);

    partial void OnManifestIconFailedChanged(bool value) => _ = LoadFallbackIconAsync();

    private async Task LoadFallbackIconAsync() =>
        FallbackIcon = await LocalAddonIcon.LoadAsync(_install.AddOnsPath, _addon.FolderName, _logger).ConfigureAwait(true);

    public string AddonId => _addon.Id;

    public string HiddenId => _addon.Id;

    public string DisplayName => _addon.DisplayName;

    public string FolderName => _addon.FolderName;

    public string FolderLine => (_addon.Folders ?? _status?.Release?.Folders ?? []).Count(folder => !string.Equals(folder, FolderName, StringComparison.OrdinalIgnoreCase)) is var others and > 0
        ? $"{FolderName} + {others} folder{(others == 1 ? "" : "s")}"
        : FolderName;

    public bool IsDistributable => _status?.Release?.Distributable ?? true;

    public string Source => _addon.Source;

    public string InstalledRunText => InstalledVersionShort ?? "";

    public string NotInstalledRunText => IsInstalled ? "" : "Not installed";

    public string VersionPairTip => $"{InstalledVersion ?? "Not installed"} → {AvailableVersion}";

    public string? InstalledVersionShort => ShortVersion(InstalledVersion);

    public string? AvailableVersionShort => ShortVersion(AvailableVersion);

    private static string? ShortVersion(string? version) => version?.Replace("-pre-release.", "-", StringComparison.Ordinal);

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
    public partial bool IsIgnored { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestedXpSignInVisibility), nameof(StatusTextVisibility), nameof(UpToDateVisibility))]
    public partial bool NeedsRestedXpSignIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestedXpSignInVisibility), nameof(StatusTextVisibility), nameof(UpToDateVisibility))]
    public partial bool HasGuidesFeature { get; set; }

    public Action? RestedXpSignInRequested { get; set; }

    public Visibility RestedXpSignInVisibility => When(NeedsRestedXpSignIn && IsInstalled && !IsHidden && HasGuidesFeature);

    public bool IsClientRunning { get; set; }

    public DateTimeOffset ReloadPendingSince { get; private set; }

    public Visibility ReloadHintVisibility => When(NeedsReload);

    public bool CanAutoApply => IsAdmin && IsDistributable
        && (State == AddonRowState.UpdateAvailable || (State == AddonRowState.Missing && _addon.AutoInstall));

    public bool HasUpdateAvailable =>
        _status?.Release is { } release && Channel is not null && TocFile.HasUpdate(release.Version, InstalledVersion);

    public bool IsPendingUpdate => !IsHidden && !IsIgnored && IsDistributable && State == AddonRowState.UpdateAvailable;

    public bool IsInstalled => InstalledVersion is not null;

    public bool HasNoReleases => _status is { Channel: null };

    private bool HasRelease => _status?.Release is not null;

    private bool IsIgnoredUpdate => IsIgnored && State == AddonRowState.UpdateAvailable;

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
        _ when !IsDistributable => $"{(State == AddonRowState.Missing ? "Get" : "Update")} on {Source}",
        _ when State == AddonRowState.Missing => "Install",
        _ when RecordedChannelDiffers && Channel is { } channel => $"Switch to {channel}",
        _ => "Update",
    };

    public Style ActionStyle => (Style)Application.Current.Resources[
        State == AddonRowState.Missing ? "DefaultButtonStyle" : "AccentButtonStyle"];

    public string ProgressText => UpdateProgress.ToString("P0", CultureInfo.CurrentCulture);

    public string UpdatingLine => $"Installing {_status?.Release?.Version}, verifying download";

    public string ReleasedText => _status?.Release is { } release
        ? $"Released {RelativeTime.Describe(release.Released, DateTimeOffset.Now)}"
        : "";

    public Brush NewVersionBrush => (Brush)Application.Current.Resources[
        IsIgnoredUpdate ? "TextFillColorTertiaryBrush" : "AvailableVersionBrush"];

    private bool ShowsVersionPair =>
        State is AddonRowState.UpdateAvailable or AddonRowState.Missing || (State == AddonRowState.Failed && HasUpdateAvailable);

    public Visibility VersionPairVisibility => When(ShowsVersionPair);

    private bool IsStacked => IsVersionStacked && ShowsVersionPair;

    public Visibility WideVersionPairVisibility => When(ShowsVersionPair && !IsStacked);

    public Visibility StackedVersionPairVisibility => When(IsStacked);

    private bool ShowsReleased => State is AddonRowState.UpdateAvailable or AddonRowState.Missing && !IsIgnoredUpdate;

    public Visibility ReleasedVisibility => When(ShowsReleased && !IsCompact && !IsStacked);

    public Visibility CurrentVersionVisibility => When(State == AddonRowState.Current);

    public Visibility NoReleasesVisibility => When(State == AddonRowState.NoReleases);

    public Visibility UpdatingVisibility => When(State == AddonRowState.Updating);

    public Visibility FailedVisibility => When(State == AddonRowState.Failed);

    private bool HasChannelChoice => Channel is not null && _status!.Releases.Count(release => release.Value is not null) > 1;

    public Visibility ChannelChipVisibility => When(HasChannelChoice);

    public Visibility SingleChannelVisibility => When(Channel is not null && !HasChannelChoice);

    public string SingleChannelTip => $"Only {Channel} builds are published for {DisplayName}.";

    public Visibility NoChannelVisibility => When(Channel is null);

    public Visibility ActionVisibility =>
        When(!IsHidden && IsAdmin && (State == AddonRowState.Missing || (!IsIgnored && State == AddonRowState.UpdateAvailable)));

    public AddonRowStatus Status => true switch
    {
        _ when IsBusy => AddonRowStatus.Updating,
        _ when HasFailed => AddonRowStatus.Failed,
        _ when IsHidden => AddonRowStatus.Hidden,
        _ when IsIgnoredUpdate => AddonRowStatus.Ignored,
        _ => State switch
        {
            AddonRowState.Missing => AddonRowStatus.NotInstalled,
            AddonRowState.NoReleases => AddonRowStatus.NoReleases,
            AddonRowState.UpdateAvailable => RecordedChannelDiffers ? AddonRowStatus.Switch : AddonRowStatus.UpdateAvailable,
            _ => AddonRowStatus.UpToDate,
        },
    };

    public int StatusRank => (int)Status;

    public string StatusText => Status switch
    {
        AddonRowStatus.UpdateAvailable => "Update available",
        AddonRowStatus.Switch => $"Switch to {Channel} pending",
        AddonRowStatus.Updating => "Updating",
        AddonRowStatus.Failed => "Failed",
        AddonRowStatus.NotInstalled => "Not installed",
        _ => "-",
    };

    public Brush StatusBrush => (Brush)Application.Current.Resources[Status switch
    {
        AddonRowStatus.UpdateAvailable or AddonRowStatus.Switch => "AvailableVersionBrush",
        AddonRowStatus.Updating => "TextFillColorSecondaryBrush",
        AddonRowStatus.Failed => "SystemFillColorCriticalBrush",
        _ => "TextFillColorTertiaryBrush",
    }];

    private bool ShowsActionButton =>
        ActionVisibility == Visibility.Visible || RestedXpSignInVisibility == Visibility.Visible || State == AddonRowState.Failed;

    public Visibility StatusTextVisibility =>
        When(!ShowsActionButton && Status is not (AddonRowStatus.UpToDate or AddonRowStatus.Ignored or AddonRowStatus.Hidden));

    public Visibility UpToDateVisibility => When(!ShowsActionButton && Status == AddonRowStatus.UpToDate);

    public Visibility IgnoredPillVisibility => When(Status == AddonRowStatus.Ignored);

    public IReadOnlyList<ChangelogBlock> Changelog => Changelogs.For(_status?.Release);

    public Visibility ChangelogVisibility =>
        When(Changelog.Count > 0 && (VersionPairVisibility == Visibility.Visible || State == AddonRowState.Current));

    public string ChangelogTitle => $"{DisplayName} {AvailableVersion}";

    [ObservableProperty]
    public partial DateTimeOffset? LastUpdated { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionCellTip), nameof(HasVersionCellTip))]
    public partial string? VersionTip { get; private set; }

    [ObservableProperty]
    public partial bool IsCompact { get; set; }

    [ObservableProperty]
    public partial bool IsVersionStacked { get; set; }

    private string? TipChannelLine => IsCompact && IsStacked && Channel is not null
        ? HasChannelChoice ? $"{Channel} channel" : SingleChannelTip
        : null;

    public string? VersionCellTip =>
        string.Join("\n", new[] { VersionTip, (IsCompact || IsStacked) && ShowsReleased ? ReleasedText : null, TipChannelLine }
            .Where(line => !string.IsNullOrEmpty(line))) is { Length: > 0 } tip ? tip : null;

    public bool HasVersionCellTip => VersionCellTip is not null;

    public Visibility CompactChannelChipVisibility => When(IsCompact && !IsStacked && HasChannelChoice);

    public Visibility CompactSingleChannelVisibility => When(IsCompact && !IsStacked && Channel is not null && !HasChannelChoice);

    public Visibility NoticeVisibility =>
        When(State != AddonRowState.Failed && !string.IsNullOrEmpty(StatusMessage));

    [ObservableProperty]
    public partial string? OutOfDateTip { get; private set; }

    public Visibility OutOfDateVisibility => When(OutOfDateTip is not null);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UninstallErrorVisibility))]
    public partial string? UninstallError { get; private set; }

    public Visibility UninstallErrorVisibility => When(UninstallError is not null && State != AddonRowState.Failed);

    public Visibility UninstallVisibility => When(IsInstalled && !IsBusy && !_addon.AutoInstall);

    private bool CanIgnoreOrHide => !_addon.AutoInstall;

    public Visibility IgnoreVisibility => When(CanIgnoreOrHide && (IsIgnored || (IsInstalled && HasRelease)));

    public string IgnoreLabel => IsIgnored ? "Resume updates" : "Ignore updates";

    public Visibility HideVisibility => When(CanIgnoreOrHide);

    public string HideLabel => IsHidden ? "Show addon" : "Hide addon";

    public Visibility ReleaseActionsVisibility => When(IsInstalled && HasRelease && IsDistributable);

    public Visibility InstalledActionsVisibility => When(IsInstalled);

    public Visibility OverflowVisibility => When(CanIgnoreOrHide || IsInstalled || HasChannelChoice);

    public Visibility HiddenPillVisibility => When(Status == AddonRowStatus.Hidden);

    private bool RecordedChannelDiffers =>
        Record is { } record && Channel is not null && !string.Equals(record.Channel, Channel, StringComparison.OrdinalIgnoreCase);

    private string Key => AppStateStore.Key(_install.FlavourPath, _addon.Id);

    private InstalledAddonRecord? Record => _stateStore.Load().Installs.GetValueOrDefault(Key);

    private string AddonFolderPath => Path.Combine(_install.AddOnsPath, _addon.FolderName);

    private string TocPath => (Directory.Exists(AddonFolderPath) ? LocalAddons.TopLevelToc(AddonFolderPath, _addon.FolderName, TocFile.InterfaceNumber(_install.ClientVersion)) : null)
        ?? Path.Combine(AddonFolderPath, $"{_addon.FolderName}.toc");

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshInstalledVersion()
    {
        var record = _stateStore.Load().Installs.GetValueOrDefault(Key);
        InstalledVersion = record?.Version ?? TocFile.ReadVersion(TocPath)
            ?? (Source == CurseForgeAddons.Source && Directory.Exists(AddonFolderPath) ? "unknown" : null);
        _ = RefreshLastUpdatedAsync(record);
    }

    private async Task RefreshLastUpdatedAsync(InstalledAddonRecord? record)
    {
        var generation = ++_lastUpdatedGeneration;
        var at = record?.InstalledAt;
        if (at is null && InstalledVersion is not null)
        {
            at = await Task.Run(() => TocTime.LastWrite(_install.AddOnsPath, _addon.FolderName)).ConfigureAwait(true);
        }

        if (generation != _lastUpdatedGeneration)
        {
            return;
        }

        LastUpdated = at;
        VersionTip = TocTime.Describe(record is null ? "Installed" : "Updated", at);
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

    partial void OnInstalledVersionChanged(string? value)
    {
        OutOfDateTip = value is null ? null : _outOfDateTip(TocPath);
        NotifyDerived();
    }

    partial void OnAvailableVersionChanged(string? value) => NotifyDerived();

    partial void OnIsAdminChanged(bool value) => NotifyDerived();

    partial void OnIsBusyChanged(bool value) => NotifyDerived();

    partial void OnHasFailedChanged(bool value) => NotifyDerived();

    partial void OnStatusMessageChanged(string? value) => NotifyDerived();

    partial void OnIsHiddenChanged(bool value) => NotifyDerived();

    partial void OnIsIgnoredChanged(bool value) => NotifyDerived();

    partial void OnIsCompactChanged(bool value) => NotifyDerived();

    partial void OnIsVersionStackedChanged(bool value) => NotifyDerived();

    private void NotifyDerived()
    {
        foreach (var name in DerivedNames)
        {
            OnPropertyChanged(name);
        }

        UpdateCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
    }

    private bool CanUpdate => !IsHidden && (!IsIgnored || !IsInstalled) && IsAdmin && HasUpdateAvailable;

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

        if (!release.Distributable)
        {
            if (Uri.TryCreate(release.Website ?? _addon.Website, UriKind.Absolute, out var website) && website.Scheme == Uri.UriSchemeHttps)
            {
                Process.Start(new ProcessStartInfo(website.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }

            return;
        }

        HasFailed = false;
        StatusMessage = null;
        UninstallError = null;
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
            state.Installs[Key] = new InstalledAddonRecord(release.Version, channel, release.Sha256, DateTimeOffset.Now, release.Sha1);
            _stateStore.Save(state);
            await InGameIcon.EnsureAsync(_updater, _install.AddOnsPath, _addon, _logger).ConfigureAwait(true);

            RefreshInstalledVersion();
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

    public Task EnsureInGameIconAsync() =>
        IsBusy || IsHidden || !IsInstalled ? Task.CompletedTask : InGameIcon.EnsureAsync(_updater, _install.AddOnsPath, _addon, _logger);

    private bool CanUninstall => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        try
        {
            var folders = AddonUpdater.InstallFolders(_addon, _status?.Release).OrderBy(folder => !string.Equals(folder, FolderName, StringComparison.OrdinalIgnoreCase)).ToList();
            var described = folders.Count == 1 ? folders[0] : $"{folders[0]} and {folders.Count - 1} more folder{(folders.Count == 2 ? "" : "s")}";
            if (!await _confirmUninstall(DisplayName, described).ConfigureAwait(true) || IsBusy)
            {
                return;
            }

            AddonUpdater.Uninstall(_install.AddOnsPath, folders);
            var state = _stateStore.Load();
            state.Installs.Remove(Key);
            state.IgnoredAddons.RemoveAll(key => string.Equals(key, Key, StringComparison.OrdinalIgnoreCase));
            _stateStore.Save(state);
            IsIgnored = false;
            NeedsReload = false;
            HasFailed = false;
            UninstallError = null;
            RefreshInstalledVersion();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            UninstallError = $"Uninstall failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = Directory.Exists(AddonFolderPath) ? AddonFolderPath : _install.AddOnsPath;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    [RelayCommand]
    private void ChangeChannel() => _changeChannelRequested(AddonId);

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

    [RelayCommand]
    private void ToggleIgnored()
    {
        var state = _stateStore.Load();
        if (IsIgnored)
        {
            state.IgnoredAddons.RemoveAll(key => string.Equals(key, Key, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            state.IgnoredAddons.Add(Key);
        }

        _stateStore.Save(state);
        IsIgnored = !IsIgnored;
    }
}
