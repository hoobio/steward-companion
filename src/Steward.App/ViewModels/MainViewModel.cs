using System.Collections.ObjectModel;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Storage.Pickers;

namespace Steward.App.ViewModels;

public enum GateFailure
{
    None,
    Timeout,
    SessionExpired,
    Unreachable,
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private static readonly string[] SummaryNames =
    [
        nameof(InstallCount),
        nameof(AddonCount),
        nameof(UpdateCount),
        nameof(HeaderSubtitle),
        nameof(BannerBrush),
        nameof(BannerGlyph),
        nameof(BannerTitle),
        nameof(BannerDetail),
        nameof(BannerVisibility),
        nameof(UpdateAllVisibility),
        nameof(CheckAgainVisibility),
        nameof(NoInstallsVisibility),
        nameof(IsAnyRowBusy),
        nameof(InstallsDescription),
    ];

    private readonly ISessionService _sessionService;
    private readonly GigagrugClient _gigagrugClient;
    private readonly AddonUpdater _addonUpdater;
    private readonly AppStateStore _stateStore;
    private readonly IReadOnlyList<ManagedAddon> _addons;
    private readonly IReadOnlyDictionary<string, string> _supportedProducts;
    private readonly Dictionary<string, AddonChannelStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, AddonRelease?>> _releases =
        new(StringComparer.OrdinalIgnoreCase);

    private DispatcherQueueTimer? _recheckTimer;
    private CancellationTokenSource? _signInCts;
    private DateTimeOffset _lastPass;
    private bool _isChecking;
    private bool _isAutoApplying;
    private bool _isLoadingState;
    private ImageSource? _avatarImage;

    public MainViewModel(
        ISessionService sessionService,
        GigagrugClient gigagrugClient,
        AddonUpdater addonUpdater,
        AppStateStore stateStore,
        IReadOnlyList<ManagedAddon> addons,
        IReadOnlyDictionary<string, string> supportedProducts)
    {
        ArgumentNullException.ThrowIfNull(addons);
        ArgumentNullException.ThrowIfNull(supportedProducts);

        _sessionService = sessionService;
        _gigagrugClient = gigagrugClient;
        _addonUpdater = addonUpdater;
        _stateStore = stateStore;
        _addons = addons;
        _supportedProducts = supportedProducts;

        foreach (var addon in addons)
        {
            AddonChannels.Add(new AddonChannelViewModel(addon, stateStore, OnChannelChanged));
        }

        _isLoadingState = true;
        KeepInTray = stateStore.Load().KeepInTray;
        _isLoadingState = false;
    }

    public ObservableCollection<WowInstallViewModel> Installs { get; } = [];

    public ObservableCollection<AddonChannelViewModel> AddonChannels { get; } = [];

    public nint OwnerWindowHandle { get; set; }

    public Action? NavigateToSettings { get; set; }

    public Func<Task>? SavedVariablesChanged { get; set; }

    public Func<WowInstall, Task>? AfterStewardInstalled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemberInfoBarText))]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessageIsOpen))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemberInfoBarVisibility), nameof(MemberInfoBarIsOpen))]
    public partial bool IsAuthorized { get; set; }

    [ObservableProperty]
    public partial bool IsGlobalAdmin { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoInstallsVisibility))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle), nameof(BannerDetail))]
    public partial string LastCheckedRelative { get; set; } = "not checked yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GateVisibility), nameof(ShellChromeVisibility))]
    [NotifyPropertyChangedFor(nameof(MemberInfoBarVisibility), nameof(MemberInfoBarIsOpen))]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GateHeading), nameof(CancelSignInVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial bool IsSigningIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeoutVisibility), nameof(SessionExpiredVisibility), nameof(UnreachableVisibility))]
    public partial GateFailure Failure { get; set; }

    [ObservableProperty]
    public partial bool KeepInTray { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleLabel))]
    public partial string? Role { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserHandleVisibility))]
    public partial string? UserHandle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarImage))]
    public partial Uri? AvatarUri { get; set; }

    private IReadOnlyList<string> VisibleChannels => IsGlobalAdmin ? AddonChannelStatus.Ordered : ["stable", "beta"];

    public int InstallCount => Installs.Count;

    public int AddonCount => Installs.Sum(install => install.AddonRows.Count);

    public int UpdateCount => Installs.Sum(install => install.UpdateCount);

    public bool IsAnyRowBusy => Installs.Any(install => install.AddonRows.Any(row => row.IsBusy));

    public string HeaderSubtitle =>
        $"{InstallCount} World of Warcraft installs, last checked {LastCheckedRelative}";

    public Brush BannerBrush => (Brush)Application.Current.Resources[
        UpdateCount > 0 ? "CautionTintBrush" : "SuccessTintBrush"];

    public string BannerGlyph => UpdateCount > 0 ? "" : "";

    public string BannerTitle =>
        UpdateCount > 0 ? $"{UpdateCount} update{(UpdateCount == 1 ? "" : "s")} available" : "Everything is up to date";

    public string BannerDetail
    {
        get
        {
            if (UpdateCount == 0)
            {
                return $"{AddonCount} addons across {InstallCount} installs, last checked {LastCheckedRelative}";
            }

            var first = Installs
                .SelectMany(install => install.AddonRows)
                .FirstOrDefault(row => row.HasUpdateAvailable);
            if (first is null)
            {
                return string.Empty;
            }

            var line = $"{first.AddonId} {first.InstalledVersion ?? "not installed"} → {first.AvailableVersion}";
            return UpdateCount > 1 ? $"{line}, and {UpdateCount - 1} more" : line;
        }
    }

    public Visibility BannerVisibility => When(Installs.Count > 0);

    public Visibility UpdateAllVisibility => When(IsAuthorized && UpdateCount > 0);

    public Visibility CheckAgainVisibility => When(UpdateCount == 0);

    public Visibility NoInstallsVisibility => When(Installs.Count == 0 && !IsBusy);

    public bool StatusMessageIsOpen => !string.IsNullOrEmpty(StatusMessage);

    public bool MemberInfoBarIsOpen => IsSignedIn && !IsAuthorized;

    public Visibility MemberInfoBarVisibility => When(MemberInfoBarIsOpen);

    public string MemberInfoBarText =>
        $"Signed in as {UserName}. Applying addon updates needs an officer role on the guild panel. Ask an officer to raise yours.";

    public Visibility GateVisibility => When(!IsSignedIn);

    public Visibility ShellChromeVisibility => When(IsSignedIn);

    public Visibility SyncBadgeVisibility { get; } = Visibility.Collapsed;

    public Visibility TimeoutVisibility => When(Failure == GateFailure.Timeout);

    public Visibility SessionExpiredVisibility => When(Failure == GateFailure.SessionExpired);

    public Visibility UnreachableVisibility => When(Failure == GateFailure.Unreachable);

    public Visibility CancelSignInVisibility => When(IsSigningIn);

    public ImageSource? AvatarImage => _avatarImage;

    public string GateHeading => IsSigningIn ? "Waiting for Discord" : "Sign in to Steward";

    public Visibility UserHandleVisibility => When(!string.IsNullOrEmpty(UserHandle));

    public string RoleLabel => Role switch
    {
        "global" => "Global admin",
        "admin" => "Admin",
        _ => "Member",
    };

    public string VersionLabel { get; } = $"Steward {typeof(App).Assembly.GetName().Version?.ToString(3)}";

    public string InstallsDescription => $"{InstallCount} found, read from .flavor.info and .build.info";

    public string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Steward");

    public string StatePath => Path.Combine(DataFolder, "state.json");

    public string AboutDescription => $"{VersionLabel}, installed to {DataFolder}";

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public void Dispose()
    {
        _recheckTimer?.Stop();
        _signInCts?.Dispose();
        _signInCts = null;
        DisposeInstalls();
    }

    private void DisposeInstalls()
    {
        foreach (var install in Installs)
        {
            install.RowsChanged -= OnInstallRowsChanged;
            install.Dispose();
        }

        Installs.Clear();
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsSignedIn = _sessionService.TryRestoreSession();
        if (!IsSignedIn)
        {
            return;
        }

        await LoadAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        Failure = GateFailure.None;
        IsSigningIn = true;
        _signInCts = new CancellationTokenSource();
        try
        {
            await _sessionService.SignInAsync(_signInCts.Token);
            IsSignedIn = true;
            await LoadAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            Failure = GateFailure.Timeout;
        }
        catch (HttpRequestException)
        {
            Failure = GateFailure.Unreachable;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsSigningIn = false;
            _signInCts.Dispose();
            _signInCts = null;
        }
    }

    private bool CanSignIn() => !IsSigningIn;

    [RelayCommand]
    private void CancelSignIn() => _signInCts?.Cancel();

    [RelayCommand]
    private void SignOut()
    {
        _sessionService.ClearSession();
        _recheckTimer?.Stop();
        DisposeInstalls();
        _status.Clear();
        _releases.Clear();
        UserName = null;
        UserHandle = null;
        Role = null;
        AvatarUri = null;
        IsAuthorized = false;
        IsGlobalAdmin = false;
        StatusMessage = null;
        Failure = GateFailure.None;
        IsSignedIn = false;
        RecomputeSummary();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            if (await RecheckAuthorizationAsync(cancellationToken).ConfigureAwait(true) == AuthCheckResult.SessionExpired)
            {
                return;
            }

            var added = _stateStore.Load().AddedInstalls;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var install in WowInstalls.Discover(_supportedProducts)
                .Concat(added.Select(path => WowInstalls.FromFlavourPath(path, _supportedProducts)).OfType<WowInstall>()))
            {
                if (seen.Add(install.FlavourPath))
                {
                    AddInstall(install, added.Contains(install.FlavourPath, StringComparer.OrdinalIgnoreCase));
                }
            }

            await CheckAsync(background: false, cancellationToken).ConfigureAwait(true);

            StartRecheckTimer();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RecomputeSummary();
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerWindowHandle);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        var install = WowInstalls.FromFlavourPath(folder.Path, _supportedProducts);
        if (install is null)
        {
            StatusMessage = $"{folder.Path} is not a valid WoW flavour directory.";
            return;
        }

        if (install.ProductCode is null || !_supportedProducts.ContainsKey(install.ProductCode))
        {
            StatusMessage = "Steward supports World of Warcraft: Forever only.";
            return;
        }

        if (Installs.Any(existing => string.Equals(existing.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var state = _stateStore.Load();
        if (!state.AddedInstalls.Contains(install.FlavourPath, StringComparer.OrdinalIgnoreCase))
        {
            state.AddedInstalls.Add(install.FlavourPath);
            _stateStore.Save(state);
        }

        var viewModel = AddInstall(install, isAddedByUser: true);
        viewModel.ApplyStatus(_status, background: false);
        RecomputeSummary();
    }

    [RelayCommand]
    private void Rescan()
    {
        foreach (var install in WowInstalls.Discover(_supportedProducts))
        {
            if (Installs.Any(existing => string.Equals(existing.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddInstall(install, isAddedByUser: false).ApplyStatus(_status, background: false);
        }

        RecomputeSummary();
    }

    private void RemoveInstall(WowInstallViewModel install)
    {
        Installs.Remove(install);
        install.RowsChanged -= OnInstallRowsChanged;
        install.Dispose();
        _stateStore.Save(AppStateStore.RemoveInstall(_stateStore.Load(), install.FlavourPath));
        RecomputeSummary();
    }

    private bool CanUpdateAll => IsAuthorized && UpdateCount > 0 && !IsAnyRowBusy;

    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private async Task UpdateAllAsync()
    {
        foreach (var row in Installs.SelectMany(install => install.AddonRows).ToList())
        {
            if (row.UpdateCommand.CanExecute(null))
            {
                await row.UpdateCommand.ExecuteAsync(null).ConfigureAwait(true);
            }
        }

        RecomputeSummary();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            await CheckAsync(background: false, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            RecomputeSummary();
        }
    }

    private async Task CheckAsync(bool background, CancellationToken cancellationToken)
    {
        var succeeded = true;
        try
        {
            var state = _stateStore.Load();
            foreach (var addon in _addons)
            {
                var releases = await _addonUpdater.ProbeChannelsAsync(addon, VisibleChannels, cancellationToken)
                    .ConfigureAwait(true);
                _releases[addon.Id] = releases;
                _status[addon.Id] = AddonChannelStatus.Resolve(state.Channels.GetValueOrDefault(addon.Id), releases);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException)
        {
            StatusMessage = ex.Message;
            succeeded = false;
        }

        ApplyStatus(background);

        if (succeeded)
        {
            _lastPass = DateTimeOffset.Now;
            UpdateLastCheckedText();
        }

        RecomputeSummary();

        RefreshClients();
        try
        {
            await AutoApplyAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException)
        {
            StatusMessage = ex.Message;
        }

        await NotifySavedVariablesChangedAsync().ConfigureAwait(true);
    }

    private void RefreshClients()
    {
        foreach (var install in Installs)
        {
            install.RefreshClientRunning();
        }
    }

    private async Task AutoApplyAsync()
    {
        if (_isAutoApplying || IsSigningIn || !IsSignedIn)
        {
            return;
        }

        _isAutoApplying = true;
        try
        {
            foreach (var install in Installs.ToList())
            {
                foreach (var row in install.AddonRows.ToList())
                {
                    if (row.CanAutoApply)
                    {
                        await row.UpdateCommand.ExecuteAsync(null).ConfigureAwait(true);
                    }
                }
            }
        }
        finally
        {
            _isAutoApplying = false;
            RecomputeSummary();
        }
    }

    private void ApplyStatus(bool background)
    {
        foreach (var install in Installs)
        {
            install.ApplyStatus(_status, background);
        }

        ApplyChannelStatus();
    }

    private void ApplyChannelStatus()
    {
        foreach (var channel in AddonChannels)
        {
            if (_status.TryGetValue(channel.AddonId, out var status))
            {
                channel.Apply(status, IsGlobalAdmin, IsAuthorized);
            }
        }
    }

    partial void OnKeepInTrayChanged(bool value)
    {
        if (_isLoadingState)
        {
            return;
        }

        _stateStore.Save(_stateStore.Load() with { KeepInTray = value });
    }

    partial void OnAvatarUriChanged(Uri? value) => _avatarImage = value is null ? null : new BitmapImage(value);

    private void OnChannelChanged(string addonId, string channel)
    {
        if (!_releases.TryGetValue(addonId, out var releases))
        {
            return;
        }

        _status[addonId] = AddonChannelStatus.Resolve(channel, releases);
        ApplyStatus(background: false);
        RecomputeSummary();
    }

    private WowInstallViewModel AddInstall(WowInstall install, bool isAddedByUser)
    {
        var viewModel = new WowInstallViewModel(
            install,
            _addons,
            _addonUpdater,
            _stateStore,
            EnsureAuthorizedForActionAsync,
            () => NavigateToSettings?.Invoke(),
            RemoveInstall,
            OnClientExited,
            wowInstall => AfterStewardInstalled?.Invoke(wowInstall) ?? Task.CompletedTask)
        {
            IsAddedByUser = isAddedByUser,
        };
        viewModel.SetIsAdmin(IsAuthorized);
        viewModel.RowsChanged += OnInstallRowsChanged;
        Installs.Add(viewModel);
        return viewModel;
    }

    private void OnInstallRowsChanged(object? sender, EventArgs e) => RecomputeSummary();

    private void OnClientExited(WowInstallViewModel install) => _ = NotifySavedVariablesChangedAsync();

    private Task NotifySavedVariablesChangedAsync() => SavedVariablesChanged?.Invoke() ?? Task.CompletedTask;

    private void RecomputeSummary()
    {
        foreach (var name in SummaryNames)
        {
            OnPropertyChanged(name);
        }

        UpdateAllCommand.NotifyCanExecuteChanged();
    }

    private void StartRecheckTimer()
    {
        if (_recheckTimer is null)
        {
            _recheckTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _recheckTimer.Interval = TickInterval;
            _recheckTimer.IsRepeating = true;
            _recheckTimer.Tick += (_, _) => OnTick();
        }

        _recheckTimer.Start();
    }

    private void OnTick()
    {
        UpdateLastCheckedText();
        _ = NotifySavedVariablesChangedAsync();
        if (!_isChecking && DateTimeOffset.Now - _lastPass >= RecheckInterval)
        {
            _ = RunBackgroundPassAsync();
            return;
        }

        RefreshClients();
        _ = AutoApplyAsync();
    }

    private async Task RunBackgroundPassAsync()
    {
        _isChecking = true;
        try
        {
            var result = await RecheckAuthorizationAsync(CancellationToken.None).ConfigureAwait(true);
            if (result != AuthCheckResult.SessionExpired)
            {
                await CheckAsync(background: true, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            _isChecking = false;
        }
    }

    private void UpdateLastCheckedText()
    {
        var elapsed = DateTimeOffset.Now - _lastPass;
        LastCheckedRelative = true switch
        {
            _ when _lastPass == default => "not checked yet",
            _ when elapsed < TimeSpan.FromMinutes(1) => "just now",
            _ when elapsed < TimeSpan.FromMinutes(60) =>
                $"{(int)elapsed.TotalMinutes} minute{((int)elapsed.TotalMinutes == 1 ? "" : "s")} ago",
            _ => $"{(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours == 1 ? "" : "s")} ago",
        };
    }

    private async Task<bool> EnsureAuthorizedForActionAsync(CancellationToken cancellationToken) =>
        await RecheckAuthorizationAsync(cancellationToken).ConfigureAwait(true) == AuthCheckResult.Authorized;

    private async Task<AuthCheckResult> RecheckAuthorizationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _gigagrugClient.GetMeAsync(cancellationToken).ConfigureAwait(true);
            UserName = me.User.Name;
            UserHandle = me.User.Username is { Length: > 0 } u ? $"@{u}" : null;
            Role = me.User.Role;
            AvatarUri = Uri.TryCreate(me.User.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null;
            IsAuthorized = GigagrugClient.IsAdmin(me);
            StatusMessage = null;

            // Client-side gate only: gigagrug does not restrict who can fetch the unstable manifest.
            IsGlobalAdmin = GigagrugClient.IsGlobalAdmin(me);

            PropagateAuthorized();
            return IsAuthorized ? AuthCheckResult.Authorized : AuthCheckResult.NotAuthorized;
        }
        catch (SessionExpiredException)
        {
            _sessionService.ClearSession();
            IsAuthorized = false;
            IsGlobalAdmin = false;
            UserName = null;
            UserHandle = null;
            Role = null;
            AvatarUri = null;
            StatusMessage = null;
            IsSignedIn = false;
            Failure = GateFailure.SessionExpired;
            _recheckTimer?.Stop();
            PropagateAuthorized();
            return AuthCheckResult.SessionExpired;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Failure = GateFailure.Unreachable;
            StatusMessage = ex.Message;
            return AuthCheckResult.NotAuthorized;
        }
    }

    private void PropagateAuthorized()
    {
        foreach (var install in Installs)
        {
            install.SetIsAdmin(IsAuthorized);
        }

        ApplyChannelStatus();
    }

    private enum AuthCheckResult
    {
        Authorized,
        NotAuthorized,
        SessionExpired,
    }
}
