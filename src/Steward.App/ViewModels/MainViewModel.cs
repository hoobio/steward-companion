using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.App.Views;
using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using Windows.Management.Deployment;
using Windows.Services.Store;

namespace Steward.App.ViewModels;

public enum GateFailure
{
    None,
    Timeout,
    SessionExpired,
    Unreachable,
    NotAuthorized,
    SignInFailed,
}

public enum LiveUpdatesState
{
    Hidden,
    Live,
    Reconnecting,
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan GuideCheckInterval = TimeSpan.FromHours(3);

    private static readonly TimeSpan StoreCheckInterval = TimeSpan.FromHours(1);

    private static readonly TimeSpan GuildSyncFallbackInterval = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan GuildEventDebounce = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan AccessEventDebounce = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan EventStreamMinBackoff = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan EventStreamMaxBackoff = TimeSpan.FromMinutes(5);

    private const string StartupTaskId = "StewardStartup";

    private static readonly string[] SummaryNames =
    [
        nameof(InstallCount),
        nameof(NoInstallsVisibility),
        nameof(IsAnyRowBusy),
        nameof(InstallsDescription),
        nameof(GuidesVisibility),
        nameof(SyncVisibility),
        nameof(HasSyncFeature),
        nameof(IsProfessionsOnlySync),
    ];

    private readonly ISessionService _sessionService;
    private readonly GigagrugClient _gigagrugClient;
    private readonly GigagrugGuildSyncApi _guildSyncApi;
    private readonly AddonUpdater _addonUpdater;
    private readonly AppUpdater _appUpdater;
    private readonly AppStateStore _stateStore;
    private readonly IReadOnlyList<ManagedAddon> _addons;
    private readonly IReadOnlyDictionary<string, string> _supportedProducts;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dictionary<string, AddonChannelStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, AddonRelease?>> _releases =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _features = new(StringComparer.Ordinal);
    private readonly HashSet<string> _guildFeatures = new(StringComparer.Ordinal);
    private IReadOnlyList<AdminGuild> _meGuilds = [];

    private DispatcherQueueTimer? _recheckTimer;
    private CancellationTokenSource? _signInCts;
    private string? _guildId;
    private string? _userId;
    private string? _characterRowsGuildId;
    private DateTimeOffset _lastPass;
    private DateTimeOffset _lastGuideCheck;
    private TimeSpan _nextGuideCheckDue = GuideCheckInterval;
    private DateTimeOffset _lastStoreCheck;
    private StoreContext? _storeContext;
    private IReadOnlyList<StorePackageUpdate>? _storeUpdates;
    private Task? _storeInstall;
    private DateTimeOffset _lastGuildSync;
    private DateTimeOffset _lastDirectorySync;
    private DateTimeOffset _lastBannersSync;
    private IReadOnlyList<Banner> _allBanners = [];
    private readonly List<BannerViewModel> _localBanners = [];
    private SyncDirectory? _lastDirectory;
    private IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>>? _lastMemberCatalogue;
    private SyncPayload? _lastOfficerPayload;
    private CancellationTokenSource? _eventsCts;
    private string? _eventsGuildId;
    private bool _eventsUnsupported;
    private bool _isEventStreamLive;
    private CancellationTokenSource? _accessEventsCts;
    private bool _accessEventsRunning;
    private bool _isAccessEventStreamLive;
    private bool _accessEventsUnsupported;
    private bool _accessRecheckPending;
    private bool _isAccessRechecking;
    private bool _isGuildPulling;
    private bool _guildPullPending;
    private bool _isBannerRefreshing;
    private bool _bannerRefreshPending;
    private bool _isPushing;
    private bool? _pendingPush;
    private bool _isChecking;
    private bool _isAutoApplying;
    private bool _isLoadingState;
    private ImageSource? _avatarImage;

    public MainViewModel(
        ISessionService sessionService,
        GigagrugClient gigagrugClient,
        GigagrugGuildSyncApi guildSyncApi,
        AddonUpdater addonUpdater,
        AppUpdater appUpdater,
        AppStateStore stateStore,
        IReadOnlyList<ManagedAddon> addons,
        IReadOnlyDictionary<string, string> supportedProducts,
        RestedXpService restedXpService,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(addons);
        ArgumentNullException.ThrowIfNull(supportedProducts);

        _sessionService = sessionService;
        _gigagrugClient = gigagrugClient;
        _guildSyncApi = guildSyncApi;
        _addonUpdater = addonUpdater;
        _appUpdater = appUpdater;
        _stateStore = stateStore;
        LastAppUpdateCheck = stateStore.Load().AppUpdateCheck;
        _addons = addons;
        _supportedProducts = supportedProducts;
        _logger = logger;

        var state = stateStore.Load();
        _isLoadingState = true;
        MinimizeToTray = state.MinimizeToTray;
        CloseToTray = state.CloseToTray;
        AutoUpdateIndex = AppStateStore.ParseAutoUpdate(state.AutoUpdate) switch
        {
            AutoUpdateMode.Always => 0,
            AutoUpdateMode.Never => 2,
            _ => 1,
        };
        StartWithWindows = !App.IsPackaged && StartupRegistration.IsEnabled();
        _isLoadingState = false;

        RestedXp = new RestedXpViewModel(restedXpService, () => HasGuidesFeature);
        RestedXp.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RestedXpViewModel.IsSignedIn))
            {
                SyncRestedXpRows();
                OnPropertyChanged(nameof(GuidesVisibility));
            }
        };

        Sync = new SyncViewModel(this)
        {
            StateChanged = () => OnPropertyChanged(nameof(SyncBadgeVisibility)),
        };
        SavedVariablesChanged = Sync.ReloadAsync;
        AfterStewardInstalled = _ => HasStewardFeature ? Sync.WriteGeneratedFileAsync() : WriteMeAfterInstallAsync();
    }

    private Task WriteMeAfterInstallAsync()
    {
        if (IsSignedIn && _userId is { } userId)
        {
            WriteMe(Installs.Select(install => install.Install), new SyncMe(userId, Role, [.. _features]));
        }

        return Task.CompletedTask;
    }

    public SyncViewModel Sync { get; }

    public RestedXpViewModel RestedXp { get; }

    public ObservableCollection<WowInstallViewModel> Installs { get; } = [];

    public ObservableCollection<AddonChannelViewModel> AddonChannels { get; } = [];

    public ObservableCollection<GuildOptionViewModel> Guilds { get; } = [];

    public nint OwnerWindowHandle { get; set; }

    public Action? NavigateToAddons { get; set; }

    public Action<string>? NavigateToPageTag { get; set; }

    public Action? ShowRestedXpSignIn { get; set; }

    public bool IsGuidesPreview { get; set; }

    public Action? QuitRequested { get; set; }

    public Func<Task>? SavedVariablesChanged { get; set; }

    public Func<WowInstall, Task>? AfterStewardInstalled { get; set; }

    [ObservableProperty]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessageIsOpen))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsAuthorized { get; set; }

    [ObservableProperty]
    public partial bool IsGlobalAdmin { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoInstallsVisibility))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedText))]
    public partial string LastCheckedRelative { get; set; } = "not checked yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GateVisibility), nameof(ShellChromeVisibility))]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GateHeading), nameof(CancelSignInVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial bool IsSigningIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingSignInVisibility))]
    [NotifyCanExecuteChangedFor(nameof(OpenBrowserAgainCommand), nameof(CopyLinkCommand))]
    public partial string? PendingSignInUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrowserOpenFailedVisibility))]
    public partial bool BrowserOpenFailed { get; set; }

    [ObservableProperty]
    public partial string? CopyLinkStatus { get; set; }

    [ObservableProperty]
    public partial string? SignInError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeoutVisibility), nameof(SessionExpiredVisibility), nameof(UnreachableVisibility), nameof(NotAuthorizedVisibility), nameof(SignInFailedVisibility), nameof(IsApiReachable), nameof(RetryVisibility))]
    public partial GateFailure Failure { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveUpdatesVisibility), nameof(LiveUpdatesLiveVisibility), nameof(LiveUpdatesReconnectingVisibility), nameof(LiveUpdatesTooltip))]
    public partial LiveUpdatesState LiveUpdatesState { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool CanStartWithWindows { get; set; } = App.IsGitHubRelease;

    [ObservableProperty]
    public partial string StartWithWindowsDescription { get; set; } = App.IsGitHubRelease
        ?"Starts Steward in the tray when you sign in to Windows"
        : "Only available in a released build";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoUpdateDescription))]
    public partial int AutoUpdateIndex { get; set; }

    public string AutoUpdateDescription => AutoUpdateIndex switch
    {
        0 => "Updates install as soon as they're available, even while the game is running",
        2 => "Nothing installs automatically; use the row button or Update all",
        _ => "Updates wait until the game client isn't running",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoreListingVisibility), nameof(StoreSwitchVisibility))]
    public partial bool? IsStoreAppInstalled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleLabel))]
    public partial string? Role { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuildSubtitle))]
    public partial GuildOptionViewModel? SelectedGuild { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserHandleVisibility))]
    public partial string? UserHandle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarImage))]
    public partial Uri? AvatarUri { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppUpdateVisibility), nameof(AppUpdateChangelogUri), nameof(AboutDescription), nameof(AboutActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(InstallAppUpdateCommand))]
    public partial AddonRelease? AppUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AboutDescription), nameof(AboutActionLabel))]
    public partial bool IsCheckingAppUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AboutDescription))]
    public partial AppUpdateCheck? LastAppUpdateCheck { get; set; }

    public Visibility AppUpdateVisibility => When(AppUpdate is not null);

#pragma warning disable CA1822 // x:Bind resolves these through a ViewModel instance
    public string AppUpdateTitle => "A Steward update is available in the Microsoft Store";

    public string AppUpdateActionLabel => "Install update";
#pragma warning restore CA1822

    public Uri? AppUpdateChangelogUri => AppUpdate is null ? null
        : new Uri("https://github.com/hoobio/steward-companion/releases/latest");

    public Visibility StoreListingVisibility => When(IsStoreAppInstalled == false);

    public Visibility StoreSwitchVisibility => When(IsStoreAppInstalled == true);

#pragma warning disable CA1822 // x:Bind resolves these through a ViewModel instance
    public Visibility CheckForUpdatesVisibility => When(App.IsGitHubRelease);
#pragma warning restore CA1822

    public string AboutActionLabel => IsCheckingAppUpdate ? "Checking" : AppUpdate is null ? "Check for a new version" : "Install update";

    private IReadOnlyList<string> VisibleChannels => IsGlobalAdmin ? AddonChannelStatus.Ordered : ["release", "pre-release"];

    public bool HasGuidesFeature => _features.Contains(GigagrugClient.GuidesFeature);

    private bool HasStewardFeature => _guildFeatures.Contains(GigagrugClient.StewardFeature);

    public bool HasSyncFeature => _guildFeatures.Contains(GigagrugClient.SyncFeature);

    public bool HasRosterFeature => _guildFeatures.Contains(GigagrugClient.RosterFeature);

    public bool HasProfessionsFeature => _guildFeatures.Contains(GigagrugClient.ProfessionsFeature);

    public SyncDirectory? LastDirectory => _lastDirectory;

    public IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>>? LastMemberCatalogue => _lastMemberCatalogue;

    public bool IsProfessionsOnlySync => HasSyncFeature && !HasStewardFeature;

    private IReadOnlyList<ManagedAddon> VisibleAddons() =>
        [.. _addons.Where(addon => addon.Features.Any(_features.Contains))];

    public int InstallCount => Installs.Count;

    public bool IsAnyRowBusy => Installs.Any(install => install.AddonRows.Any(row => row.IsBusy));

    public Visibility NoInstallsVisibility => When(Installs.Count == 0 && !IsBusy);

    public bool StatusMessageIsOpen => !string.IsNullOrEmpty(StatusMessage);

    public Visibility GateVisibility => When(!IsSignedIn);

    public Visibility ShellChromeVisibility => When(IsSignedIn);

    public Visibility SyncBadgeVisibility => When(Sync.HasWaiting);

    public Visibility GuidesVisibility => When(IsGuidesPreview
        || (HasGuidesFeature && RestedXp.IsSignedIn && Installs.Any(install => install.AddonRows.Any(row =>
            string.Equals(row.AddonId, RestedXpViewModel.AddonId, StringComparison.OrdinalIgnoreCase)
            && row.State is not (AddonRowState.Missing or AddonRowState.NoReleases)))));

    public Visibility SyncVisibility => When(HasStewardFeature || HasSyncFeature);

    public ObservableCollection<CharacterSyncRowViewModel> CharacterSyncRows { get; } = [];

    public ObservableCollection<BannerViewModel> Banners { get; } = [];

    public Visibility TimeoutVisibility => When(Failure == GateFailure.Timeout);

    public Visibility SessionExpiredVisibility => When(Failure == GateFailure.SessionExpired);

    public Visibility UnreachableVisibility => When(Failure == GateFailure.Unreachable);

    public Visibility NotAuthorizedVisibility => When(Failure == GateFailure.NotAuthorized);

    public Visibility SignInFailedVisibility => When(Failure == GateFailure.SignInFailed);

    public bool IsApiReachable => Failure != GateFailure.Unreachable;

    public Visibility LiveUpdatesVisibility => When(LiveUpdatesState != LiveUpdatesState.Hidden);

    public Visibility LiveUpdatesLiveVisibility => When(LiveUpdatesState == LiveUpdatesState.Live);

    public Visibility LiveUpdatesReconnectingVisibility => When(LiveUpdatesState == LiveUpdatesState.Reconnecting);

    public string LiveUpdatesTooltip => LiveUpdatesState == LiveUpdatesState.Reconnecting
        ? "Reconnecting to api.hoobi.io; changes still arrive on the next check"
        : "Connected to api.hoobi.io via SSE. Changes to guild data arrive instantly.";

    public Visibility RetryVisibility => When(Failure == GateFailure.Unreachable);

    public Visibility CancelSignInVisibility => When(IsSigningIn);

    public Visibility PendingSignInVisibility => When(PendingSignInUrl is not null);

    public Visibility BrowserOpenFailedVisibility => When(BrowserOpenFailed);

    public ImageSource? AvatarImage => _avatarImage;

    public string GateHeading => IsSigningIn ? "Waiting for Discord" : "Sign in to Steward";

    public Visibility UserHandleVisibility => When(!string.IsNullOrEmpty(UserHandle));

    public Visibility GuildPickerVisibility => When(Guilds.Count > 0);

    public string GuildSubtitle => SelectedGuild is { } guild
        ? $"{guild.MemberCount} members · {Guilds.Count} server{(Guilds.Count == 1 ? "" : "s")}"
        : $"{Guilds.Count} server{(Guilds.Count == 1 ? "" : "s")}";

    public string RoleLabel => Role switch
    {
        "global" => "Global admin",
        "admin" => "Admin",
        _ => "Member",
    };

    public static string InstalledVersion { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { Length: > 0 } informational
            ? informational.Split('+', 2)[0]
            : typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string Channel => !App.IsGitHubRelease ? "dev" : App.IsPackaged ? "store" : "msi";

    private static string DisplayedVersion => App.StoreDisplayVersion ?? InstalledVersion;

    public string VersionLabel { get; } = $"Steward {DisplayedVersion}{BuildSuffix}";

    public static string WindowTitle => $"Steward{BuildSuffix}";

    private static string BuildSuffix => App.IsGitHubRelease ? (App.IsPreRelease ? " (Pre-release)" : "")
        : App.BuildName == "debug" ? " (Debug)" : " (Development)";

    public string InstallsDescription => $"{InstallCount} found, read from .flavor.info and .build.info";

    public string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Steward");

    public string LogFolder => Path.Combine(DataFolder, "logs");

    public string StatePath => Path.Combine(DataFolder, "state.json");

    public string AboutDescription =>
        $"{VersionLabel}\n{AppUpdateStatus}";

    private string AppUpdateStatus => App.BuildName switch
    {
        "debug" => "Debug build, not updated automatically",
        "dev" => "Development build, rebuilt on every push",
        "msi" => "Updates come from the Microsoft Store version",
        _ when AppUpdate is not null => "An update is available",
        _ when IsCheckingAppUpdate => "Checking for updates",
        _ when LastAppUpdateCheck is { } check && check.Version == DisplayedVersion =>
            check.UpdateAvailable ? "An update is available" : "Up to date",
        _ => "Not checked for updates yet",
    };

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public void Dispose()
    {
        _recheckTimer?.Stop();
        _eventsCts?.Cancel();
        _eventsCts?.Dispose();
        _accessEventsCts?.Cancel();
        _accessEventsCts?.Dispose();
        _signInCts?.Dispose();
        _signInCts = null;
        DisposeInstalls();
        RestedXp.Dispose();
    }

    private void DisposeInstalls()
    {
        foreach (var install in Installs)
        {
            install.RowsChanged -= OnInstallRowsChanged;
            install.Dispose();
        }

        Installs.Clear();
        SelectedInstall = null;
        SyncGuideInstalls();
    }

    private void SyncGuideInstalls()
    {
        RestedXp.SetInstalls(Installs);
        SyncRestedXpRows();
    }

    private void SyncRestedXpRows()
    {
        foreach (var row in Installs.SelectMany(install => install.AddonRows)
            .Where(row => string.Equals(row.AddonId, RestedXpViewModel.AddonId, StringComparison.OrdinalIgnoreCase)))
        {
            row.RestedXpSignInRequested = () => ShowRestedXpSignIn?.Invoke();
            row.NeedsRestedXpSignIn = !RestedXp.IsSignedIn;
            row.HasGuidesFeature = HasGuidesFeature;
        }
    }

    private async Task CheckGuidesAsync()
    {
        if (!HasGuidesFeature)
        {
            return;
        }

        _lastGuideCheck = DateTimeOffset.Now;
        _nextGuideCheckDue = GuideCheckInterval + TimeSpan.FromMinutes(Random.Shared.NextDouble() * 30);
        await RestedXp.CheckAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (App.IsPackaged)
        {
            await SetStartupTaskAsync(enable: App.IsGitHubRelease ? null : false).ConfigureAwait(true);
        }

        _lastBannersSync = DateTimeOffset.Now;
        await SyncBannersAsync().ConfigureAwait(true);

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
        BrowserOpenFailed = false;
        IsSigningIn = true;
        _signInCts = new CancellationTokenSource();
        try
        {
            await _sessionService.SignInAsync(new Progress<string>(OpenSignInUrl), _signInCts.Token);
            IsSignedIn = true;
            await LoadAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Sign-in cancelled");
        }
        catch (TimeoutException ex)
        {
            _logger.Warn(ex, "Sign-in timed out");
            Failure = GateFailure.Timeout;
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn(ex, "Sign-in failed, gigagrug unreachable");
            Failure = GateFailure.Unreachable;
        }
        catch (Exception ex) when (!IsSignedIn)
        {
            _logger.Warn(ex, "Sign-in failed");
            SignInError = $"Sign-in failed: {ex.Message}";
            Failure = GateFailure.SignInFailed;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Sign-in succeeded but the follow-up load failed");
            StatusMessage = ex.Message;
        }
        finally
        {
            PendingSignInUrl = null;
            IsSigningIn = false;
            _signInCts.Dispose();
            _signInCts = null;
        }
    }

    private bool CanSignIn() => !IsSigningIn;

    [RelayCommand]
    private void CancelSignIn() => _signInCts?.Cancel();

    private void OpenSignInUrl(string url)
    {
        PendingSignInUrl = url;
        OpenBrowserAgain();
    }

    [RelayCommand(CanExecute = nameof(HasPendingSignInUrl))]
    private void OpenBrowserAgain() =>
        BrowserOpenFailed = PendingSignInUrl is { } url && !_sessionService.TryOpenBrowser(url);

    [RelayCommand(CanExecute = nameof(HasPendingSignInUrl))]
    private async Task CopyLinkAsync()
    {
        if (PendingSignInUrl is not { } url)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(url);
        Clipboard.SetContent(package);

        CopyLinkStatus = "Link copied";
        await Task.Delay(TimeSpan.FromSeconds(2));
        CopyLinkStatus = null;
    }

    private bool HasPendingSignInUrl() => PendingSignInUrl is not null;

    [RelayCommand]
    private void SignOut()
    {
        SignOutTo(GateFailure.None);
        RecomputeSummary();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            UpdateStoreAppInstalledState();

            if (await RecheckAuthorizationAsync(cancellationToken).ConfigureAwait(true)
                is AuthCheckResult.SessionExpired or AuthCheckResult.NotAuthorized)
            {
                return;
            }

            var state = _stateStore.Load();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var install in DiscoverInstalls(state))
            {
                if (seen.Add(install.FlavourPath))
                {
                    AddInstall(install, state.AddedInstalls.Contains(install.FlavourPath, StringComparer.OrdinalIgnoreCase));
                }
            }

            EnsureSelection();
            await CheckAsync(background: false, cancellationToken).ConfigureAwait(true);
            await PushCharacterSyncAsync().ConfigureAwait(true);
            _lastDirectorySync = DateTimeOffset.Now;
            await SyncDirectoryAsync().ConfigureAwait(true);
            await CheckAppUpdateAsync(forceStoreScan: true).ConfigureAwait(true);
            await CheckGuidesAsync().ConfigureAwait(true);

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
        if (await PickFolderAsync().ConfigureAwait(true) is not { } path)
        {
            return;
        }

        if (Installs.FirstOrDefault(existing => string.Equals(existing.FlavourPath, WowInstalls.FromFlavourPath(path, _supportedProducts)?.FlavourPath, StringComparison.OrdinalIgnoreCase)) is { } known)
        {
            SelectedInstall = known;
            return;
        }

        if (ValidateInstallFolder(path, editing: null) is { } error)
        {
            StatusMessage = error;
            return;
        }

        var state = _stateStore.Load();
        if (WowInstalls.FromFlavourPath(path, _supportedProducts, state.InstallProducts) is not { } install)
        {
            return;
        }

        if (!state.AddedInstalls.Contains(install.FlavourPath, StringComparer.OrdinalIgnoreCase))
        {
            state.AddedInstalls.Add(install.FlavourPath);
            _stateStore.Save(state);
        }

        var viewModel = AddInstall(install, isAddedByUser: true);
        viewModel.ApplyStatus(_status, background: false);
        SelectedInstall = viewModel;
        RecomputeSummary();
    }

    [RelayCommand]
    private void Rescan()
    {
        foreach (var install in Installs)
        {
            _ = install.RescanLocalAsync();
        }

        foreach (var install in WowInstalls.Discover(_supportedProducts, _stateStore.Load().InstallProducts))
        {
            if (Installs.Any(existing => string.Equals(existing.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddInstall(install, isAddedByUser: false).ApplyStatus(_status, background: false);
        }

        EnsureSelection();
        RecomputeSummary();
    }

    private void RemoveInstall(WowInstallViewModel install)
    {
        DetachInstall(install);
        _stateStore.Save(AppStateStore.RemoveInstall(_stateStore.Load(), install.FlavourPath));
        EnsureSelection();
        RecomputeSummary();
    }

    private void DetachInstall(WowInstallViewModel install)
    {
        Installs.Remove(install);
        install.RowsChanged -= OnInstallRowsChanged;
        install.Dispose();
        SyncGuideInstalls();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            UpdateStoreAppInstalledState();
            _lastBannersSync = DateTimeOffset.Now;
            await SyncBannersAsync().ConfigureAwait(true);

            if (await RecheckAuthorizationAsync(CancellationToken.None).ConfigureAwait(true)
                is AuthCheckResult.SessionExpired or AuthCheckResult.NotAuthorized)
            {
                return;
            }

            foreach (var install in Installs)
            {
                _ = install.RescanLocalAsync();
            }

            await CheckAsync(background: false, CancellationToken.None).ConfigureAwait(true);
            await PushCharacterSyncAsync().ConfigureAwait(true);
            _lastDirectorySync = DateTimeOffset.Now;
            await SyncDirectoryAsync().ConfigureAwait(true);
            await CheckAppUpdateAsync().ConfigureAwait(true);
            await CheckGuidesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            RecomputeSummary();
        }
    }

    private async Task CheckAppUpdateAsync(bool forceStoreScan = false)
    {
        if (forceStoreScan && (!App.IsGitHubRelease || App.IsPackaged))
        {
            await ForceStoreScanAsync().ConfigureAwait(true);
        }

        AppUpdate = App.IsPackaged && App.IsGitHubRelease
            ? await CheckStoreUpdateAsync().ConfigureAwait(true)
            : null;
    }

    private async Task ForceStoreScanAsync()
    {
        try
        {
            if (App.IsGitHubRelease)
            {
                Native.RegisterRestartForStoreUpdate(OwnerWindowHandle);
            }
            else
            {
                LogStoreCopyVersion("before");
            }

            var manager = new AppInstallManager();
            // SearchForUpdatesAsync is documented as needing a Microsoft-only private capability, but it runs unelevated from this full-trust process with no capability declared (verified 28 Sep 2026).
            var item = await manager.SearchForUpdatesAsync(_appUpdater.StoreProductId, string.Empty);
            _logger.Info(item is null
                ? "Store scan for Steward: no install item returned (the Store may still queue and install the update)"
                : $"Store scan for Steward: {item.GetCurrentStatus().InstallState}");

            if (!App.IsGitHubRelease)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                    LogStoreCopyVersion("2 minutes after");
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Store scan for Steward failed");
        }
    }

    private void LogStoreCopyVersion(string when)
    {
        var package = new PackageManager().FindPackagesForUser(string.Empty, App.PackageFamilyName).FirstOrDefault();
        _logger.Info(package is null
            ? $"Store copy of Steward not installed ({when} the scan)"
            : $"Store copy of Steward is {package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision} ({when} the scan)");
    }

    private void UpdateStoreAppInstalledState()
    {
        if (!App.IsPackaged && App.IsGitHubRelease)
        {
            IsStoreAppInstalled = new PackageManager().FindPackagesForUser(string.Empty, App.PackageFamilyName).Any();
        }
    }

    private async Task<AddonRelease?> CheckStoreUpdateAsync()
    {
        _lastStoreCheck = DateTimeOffset.Now;
        try
        {
            var context = StoreContext.GetDefault();
            WinRT.Interop.InitializeWithWindow.Initialize(context, OwnerWindowHandle);
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            _logger.Info($"Store update check: {updates.Count} update(s) available");
            _storeContext = context;
            _storeUpdates = updates;
            LastAppUpdateCheck = new AppUpdateCheck(DisplayedVersion, updates.Count > 0, DateTimeOffset.Now);
            _stateStore.Save(_stateStore.Load() with { AppUpdateCheck = LastAppUpdateCheck });
            if (updates.Count > 0 && context.CanSilentlyDownloadStorePackageUpdates && !IsAnyRowBusy
                && _storeInstall is not { IsCompleted: false })
            {
                _storeInstall = TrySilentInstallAsync(context, updates);
            }

            return updates.Count == 0 ? null : new AddonRelease(string.Empty, _appUpdater.StoreListingUri.OriginalString, string.Empty, 0, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Store update check failed");
            StatusMessage = $"Could not check the Microsoft Store for an update: {ex.Message}";
            return null;
        }
    }

    private async Task TrySilentInstallAsync(StoreContext context, IReadOnlyList<StorePackageUpdate> updates)
    {
        try
        {
            Native.RegisterRestartForStoreUpdate(OwnerWindowHandle);
            _logger.Info("Store silent update install starting");
            var result = await context.TrySilentDownloadAndInstallStorePackageUpdatesAsync(updates);
            _logger.Info($"Store silent update install result: {result.OverallState}");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Store silent update install failed");
            StatusMessage = $"Could not install the Microsoft Store update: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenStoreListing() => OpenUri(_appUpdater.StoreListingUri);

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (App.IsPackaged && App.IsGitHubRelease)
        {
            await CheckOrInstallAppUpdateAsync().ConfigureAwait(true);
            return;
        }

        if (!App.IsGitHubRelease)
        {
            _ = ForceStoreScanAsync();
        }

        OpenUri(App.IsPackaged ? _appUpdater.StoreUpdatesUri : _appUpdater.StoreListingUri);
    }

    private static void OpenUri(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.OriginalString) { UseShellExecute = true })?.Dispose();

    [RelayCommand]
    private void SwitchToStore()
    {
        try
        {
            AppUpdater.SwitchToStoreAfterExit(App.MsiUpgradeCode, Path.Combine(DataFolder, "update.log"), $"{App.PackageFamilyName}!App");
            QuitRequested?.Invoke();
        }
        catch (Win32Exception ex)
        {
            StatusMessage = $"Could not switch to the Microsoft Store version: {ex.Message}";
        }
    }

    private async Task SetStartupTaskAsync(bool? enable)
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            var state = enable switch
            {
                true => await task.RequestEnableAsync(),
                false => DisableStartupTask(task),
                null => task.State,
            };
            _isLoadingState = true;
            StartWithWindows = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            _isLoadingState = false;
            CanStartWithWindows = App.IsGitHubRelease && state is StartupTaskState.Enabled or StartupTaskState.Disabled;
            StartWithWindowsDescription = state switch
            {
                _ when !App.IsGitHubRelease => "Only available in a released build",
                StartupTaskState.EnabledByPolicy => "Turned on by your organisation's policy",
                StartupTaskState.DisabledByPolicy => "Turned off by your organisation's policy",
                StartupTaskState.DisabledByUser => "Turned off in Windows Settings > Apps > Startup",
                _ => "Starts Steward in the tray when you sign in to Windows",
            };
        }
        catch (COMException ex)
        {
            StatusMessage = $"Could not read the Start with Windows setting: {ex.Message}";
        }
    }

    private static StartupTaskState DisableStartupTask(StartupTask task)
    {
        task.Disable();
        return task.State;
    }

    [RelayCommand]
    private async Task CheckOrInstallAppUpdateAsync()
    {
        if (App.IsPackaged && App.IsGitHubRelease)
        {
            if (AppUpdate is not null)
            {
                await InstallAppUpdateAsync().ConfigureAwait(true);
            }
            else
            {
                await CheckAndConfirmAppUpdateAsync().ConfigureAwait(true);
            }

            return;
        }

        await CheckAndConfirmAppUpdateAsync().ConfigureAwait(true);
    }

    private async Task HandleStoreUpdateBannerActionAsync()
    {
        if (App.IsPackaged && App.IsGitHubRelease)
        {
            await CheckOrInstallAppUpdateAsync().ConfigureAwait(true);
            return;
        }

        OpenUri(_appUpdater.StoreUpdatesUri);
    }

    private async Task CheckAndConfirmAppUpdateAsync()
    {
        IsCheckingAppUpdate = true;
        try
        {
            await CheckAppUpdateAsync(forceStoreScan: true).ConfigureAwait(true);
        }
        finally
        {
            IsCheckingAppUpdate = false;
        }
    }

    private bool CanInstallAppUpdate => AppUpdate is not null;

    [RelayCommand(CanExecute = nameof(CanInstallAppUpdate))]
    private async Task InstallAppUpdateAsync()
    {
        if (AppUpdate is null)
        {
            return;
        }

        if (_storeContext is null || _storeUpdates is null || _storeUpdates.Count == 0)
        {
            await CheckStoreUpdateAsync().ConfigureAwait(true);
        }

        if (_storeInstall is { IsCompleted: false } inFlight)
        {
            await inFlight.ConfigureAwait(true);
            return;
        }

        if (_storeContext is null || _storeUpdates is null || _storeUpdates.Count == 0)
        {
            OpenUri(_appUpdater.StoreUpdatesUri);
            return;
        }

        _storeInstall = RequestStoreInstallAsync(_storeContext, _storeUpdates);
        await _storeInstall.ConfigureAwait(true);
    }

    private async Task RequestStoreInstallAsync(StoreContext context, IReadOnlyList<StorePackageUpdate> updates)
    {
        try
        {
            Native.RegisterRestartForStoreUpdate(OwnerWindowHandle);
            _logger.Info("Store update install requested by the user");
            var result = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            _logger.Info($"Store update install result: {result.OverallState}");
            if (result.OverallState is StorePackageUpdateState.Canceled)
            {
                return;
            }

            if (result.OverallState is not StorePackageUpdateState.Completed)
            {
                StatusMessage = $"Could not install the Microsoft Store update: {result.OverallState}";
                OpenUri(_appUpdater.StoreUpdatesUri);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Store update install failed");
            StatusMessage = $"Could not install the Microsoft Store update: {ex.Message}";
            OpenUri(_appUpdater.StoreUpdatesUri);
        }
    }

    private async Task CheckAsync(bool background, CancellationToken cancellationToken, bool pullGuildRoster = true)
    {
        var succeeded = true;
        try
        {
            var state = _stateStore.Load();
            foreach (var addon in VisibleAddons())
            {
                var releases = await _addonUpdater.ProbeChannelsAsync(addon, VisibleChannels, cancellationToken)
                    .ConfigureAwait(true);
                _releases[addon.Id] = releases;
                _status[addon.Id] = AddonChannelStatus.Resolve(state.Channels.GetValueOrDefault(addon.Id), releases, addon.Channels, addon.DefaultPreference);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException)
        {
            _logger.Warn(ex, "Addon manifest check failed");
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
            _logger.Warn(ex, "Auto-apply failed");
            StatusMessage = ex.Message;
        }

        if (pullGuildRoster)
        {
            await SyncRosterAsync().ConfigureAwait(true);
        }

        await NotifySavedVariablesChangedAsync().ConfigureAwait(true);
    }

    private void SetGuilds(IReadOnlyList<AdminGuild> guilds, AdminGuild? current)
    {
        _isLoadingState = true;
        if (!Guilds.Select(option => option.Id).SequenceEqual(guilds.Select(guild => guild.Id), StringComparer.Ordinal))
        {
            Guilds.Clear();
            foreach (var guild in guilds)
            {
                Guilds.Add(new GuildOptionViewModel(guild, RoleLabel));
            }
        }

        SelectedGuild = Guilds.FirstOrDefault(option => option.Id == current?.Id);
        foreach (var option in Guilds)
        {
            option.IsCurrent = option == SelectedGuild;
        }

        _isLoadingState = false;
        OnPropertyChanged(nameof(GuildSubtitle));
        OnPropertyChanged(nameof(GuildPickerVisibility));
    }

    partial void OnSelectedGuildChanged(GuildOptionViewModel? value)
    {
        if (_isLoadingState || value is null)
        {
            return;
        }

        _guildId = value.Id;
        _stateStore.Save(_stateStore.Load() with { GuildId = value.Id });
        UpdateGuildFeatures();
        foreach (var option in Guilds)
        {
            option.IsCurrent = option == value;
        }

        SyncCharacterSyncRows();
        UpdateEventStream();
        RecomputeSummary();
        _ = SyncRosterAsync();
        _ = NotifySelectedGuildAsync(value.Id);
    }

    private async Task NotifySelectedGuildAsync(string guildId)
    {
        try
        {
            await _gigagrugClient.SetSelectedGuildAsync(guildId, CancellationToken.None).ConfigureAwait(true);
            _logger.Info($"Told gigagrug the selected guild is {guildId}");
        }
        catch (Exception ex) when (ex is GigagrugRequestException or HttpRequestException or SessionExpiredException or OperationCanceledException)
        {
            _logger.Warn(ex, $"Could not tell gigagrug the selected guild is {guildId}");
        }
    }

    private void UpdateGuildFeatures()
    {
        var guild = _meGuilds.FirstOrDefault(g => g.Id == _guildId);
        _guildFeatures.Clear();
        _guildFeatures.UnionWith(GigagrugClient.ResolveGuildFeatures(guild, _features));
    }

    public async Task<string?> RewriteGuildDataAsync()
    {
        await SyncDirectoryAsync().ConfigureAwait(true);
        if (HasStewardFeature)
        {
            return await SyncRosterAsync(force: true).ConfigureAwait(true);
        }

        if (!IsSignedIn || _userId is null)
        {
            return null;
        }

        WriteMe(Installs.Select(install => install.Install), new SyncMe(_userId, Role, [.. _features]), force: true);
        return null;
    }

    public async Task<string?> SyncRosterAsync(bool force = false)
    {
        if (!IsSignedIn || !IsApiReachable || !HasStewardFeature || _guildId is not { } guildId)
        {
            return null;
        }

        SyncPayload payload;
        try
        {
            payload = await _guildSyncApi.PullAsync(guildId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or SessionExpiredException)
        {
            _logger.Warn(ex, $"Guild roster pull failed for {guildId}");
            var message = $"Could not pull the guild roster: {ex.Message}";
            StatusMessage = message;
            return message;
        }

        payload = payload with { Directory = _lastDirectory };
        _lastOfficerPayload = payload;
        foreach (var install in Installs)
        {
            GuildRosterSync.WriteIfChanged(install.Install, payload, _stateStore, force, _logger);
        }

        return null;
    }

    private async Task SyncDirectoryAsync()
    {
        if (!IsSignedIn || !IsApiReachable || _guildId is not { } guildId)
        {
            return;
        }

        var pullRoster = HasRosterFeature;
        var pullProfessions = HasProfessionsFeature;
        var pullCatalogue = pullProfessions || HasSyncFeature;
        if (!pullRoster && !pullProfessions && !pullCatalogue)
        {
            return;
        }

        try
        {
            IReadOnlyList<DirectoryPerson>? people = null;
            IReadOnlyList<DirectoryCharacter>? characters = null;
            IReadOnlyList<DirectoryProfessions>? professions = null;
            IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>>? catalogue = null;

            if (pullRoster)
            {
                var roster = await _gigagrugClient.GetMemberRosterAsync(guildId, CancellationToken.None).ConfigureAwait(true);
                people = roster.People;
                characters = roster.Characters;
            }

            if (pullProfessions)
            {
                var response = await _gigagrugClient.GetMemberProfessionsAsync(guildId, CancellationToken.None).ConfigureAwait(true);
                professions = response.Professions;
                catalogue = response.Catalogue;
            }
            else if (pullCatalogue)
            {
                catalogue = await _gigagrugClient.GetMemberCatalogueAsync(guildId, CancellationToken.None).ConfigureAwait(true);
            }

            _lastDirectory = new SyncDirectory(people, characters, professions);
            _lastMemberCatalogue = catalogue;
            _logger.Info(
                $"Directory sync for {guildId}: {people?.Count} people, {characters?.Count} characters, {professions?.Count} professions, {catalogue?.Count} catalogue recipe(s)");
        }
        catch (SessionExpiredException)
        {
            _logger.Info($"Directory sync for {guildId}: 401, session expired");
            SignOutTo(GateFailure.SessionExpired);
            return;
        }
        // A 403/404 means the guild's features do not allow that route, or an older gigagrug; handled quietly like the events stream, with no error bar.
        catch (Exception ex) when (ex is GigagrugRequestException or HttpRequestException or TaskCanceledException or JsonException)
        {
            var status = (ex as GigagrugRequestException)?.StatusCode;
            _logger.Warn(ex, $"Directory sync for {guildId} failed, status={status}");
            return;
        }

        if (HasStewardFeature)
        {
            return;
        }

        WriteMe(Installs.Select(install => install.Install), new SyncMe(_userId!, Role, [.. _features]));
        await NotifySavedVariablesChangedAsync().ConfigureAwait(true);
    }

    private async Task SyncBannersAsync()
    {
        try
        {
            _allBanners = await _gigagrugClient.GetBannersAsync(CancellationToken.None).ConfigureAwait(true);
        }
        // Banners work signed in or out and must never fail visibly; a failed or malformed fetch keeps the last good set.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.Warn(ex, "Banner fetch failed, keeping the last known banners");
            return;
        }

        RefreshBanners();
    }

    public void ApplyBannersPreview(IReadOnlyList<Banner> banners)
    {
        _allBanners = banners;
        RefreshBanners();
    }

    private void RefreshBanners()
    {
        var dismissed = _stateStore.Load().DismissedBanners ?? new Dictionary<string, int>();
        var active = BannerFilter.Active(_allBanners, InstalledVersion, Channel, dismissed)
            .OrderByDescending(banner => BannerLevels.Parse(banner.Level));
        Banners.Clear();
        foreach (var banner in _localBanners)
        {
            Banners.Add(banner);
        }

        foreach (var banner in active)
        {
            Banners.Add(BuildBanner(banner));
        }
    }

    // No server banner backs this, so it skips BannerFilter's id/revision dismissal bookkeeping and is never persisted.
    public void ShowLocalInfoBanner(string message)
    {
        BannerViewModel? banner = null;
        var dismiss = new RelayCommand(() =>
        {
            _localBanners.Remove(banner!);
            RefreshBanners();
        });
        banner = new BannerViewModel
        {
            Id = Guid.NewGuid().ToString(),
            Revision = 0,
            Title = message,
            Message = string.Empty,
            Background = (Brush)Application.Current.Resources["InfoTintBrush"],
            IconForeground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            IconGlyph = "",
            IsDismissible = true,
            Actions = [],
            DismissCommand = dismiss,
        };
        _localBanners.Add(banner);
        RefreshBanners();
    }

    private void DismissBanner(Banner banner)
    {
        var state = _stateStore.Load();
        var dismissed = new Dictionary<string, int>(state.DismissedBanners ?? [], StringComparer.Ordinal)
        {
            [banner.Id] = banner.Revision,
        };
        _stateStore.Save(state with { DismissedBanners = dismissed });
        RefreshBanners();
    }

    private bool IsPageVisible(string? page) => page switch
    {
        "addons" => true,
        "settings" => true,
        "sync" => SyncVisibility == Visibility.Visible,
        "guides" => GuidesVisibility == Visibility.Visible,
        _ => false,
    };

    private BannerViewModel BuildBanner(Banner banner)
    {
        var dismissCommand = new RelayCommand(() => DismissBanner(banner));
        var actions = new List<BannerActionViewModel>();
        foreach (var action in banner.Actions ?? [])
        {
            switch (BannerActions.Parse(action.Type))
            {
                case BannerActionKind.StoreUpdate:
                    actions.Add(new BannerActionViewModel { Label = action.Label, Command = new AsyncRelayCommand(HandleStoreUpdateBannerActionAsync) });
                    break;
                case BannerActionKind.OpenUrl when BannerActions.IsAllowedUrl(action.Url):
                    actions.Add(new BannerActionViewModel { Label = action.Label, Command = new RelayCommand(() => OpenUri(new Uri(action.Url!))) });
                    break;
                case BannerActionKind.Navigate when IsPageVisible(action.Page):
                    var page = action.Page!;
                    actions.Add(new BannerActionViewModel { Label = action.Label, Command = new RelayCommand(() => NavigateToPageTag?.Invoke(page)) });
                    break;
                // A dismiss action is always redundant: the close button already covers it when dismissible, and it is ignored otherwise.
            }
        }

        var (background, foreground, glyph) = BannerLevels.Parse(banner.Level) switch
        {
            BannerLevel.Success => ("SuccessTintBrush", "SystemFillColorSuccessBrush", ""),
            BannerLevel.Warning => ("CautionTintBrush", "SystemFillColorCautionBrush", ""),
            BannerLevel.Error => ("CriticalTintBrush", "SystemFillColorCriticalBrush", ""),
            _ => ("InfoTintBrush", "AccentTextFillColorPrimaryBrush", ""),
        };

        return new BannerViewModel
        {
            Id = banner.Id,
            Revision = banner.Revision,
            Title = banner.Title ?? string.Empty,
            Message = banner.Message ?? string.Empty,
            Background = (Brush)Application.Current.Resources[background],
            IconForeground = (Brush)Application.Current.Resources[foreground],
            IconGlyph = glyph,
            IsDismissible = banner.Dismissible,
            Actions = actions,
            DismissCommand = dismissCommand,
        };
    }

    private void UpdateEventStream()
    {
        var guildId = IsSignedIn && IsApiReachable && HasStewardFeature && !_eventsUnsupported ? _guildId : null;
        if (guildId == _eventsGuildId)
        {
            RecomputeLiveUpdatesState();
            return;
        }

        _eventsCts?.Cancel();
        _eventsCts?.Dispose();
        _eventsCts = null;
        _isEventStreamLive = false;
        _eventsGuildId = guildId;
        if (guildId is null)
        {
            RecomputeLiveUpdatesState();
            return;
        }

        _eventsCts = new CancellationTokenSource();
        _ = RunEventStreamAsync(guildId, _eventsCts.Token);
        RecomputeLiveUpdatesState();
    }

    private async Task RunEventStreamAsync(string guildId, CancellationToken cancellationToken)
    {
        var backoff = EventStreamMinBackoff;
        _logger.Info($"Guild event stream connecting for {guildId}");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var eventType in _gigagrugClient.StreamGuildEventsAsync(guildId, cancellationToken).ConfigureAwait(true))
                {
                    if (eventType == "ready")
                    {
                        _logger.Info($"Guild event stream ready for {guildId}");
                        _isEventStreamLive = true;
                        backoff = EventStreamMinBackoff;
                        RecomputeLiveUpdatesState();
                    }

                    if (eventType is "ready" or "roster-changed" or "members-changed" or "characters-changed")
                    {
                        QueueGuildPull();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.Info($"Guild event stream stopped for {guildId}");
                return;
            }
            catch (SessionExpiredException)
            {
                _logger.Info($"Guild event stream dropped for {guildId}: 401, session expired");
                SignOutTo(GateFailure.SessionExpired);
                return;
            }
            catch (GigagrugRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                _logger.Info($"Guild event stream giving up for {guildId}, status={ex.StatusCode}");
                _isEventStreamLive = false;
                _eventsUnsupported |= ex.StatusCode == HttpStatusCode.NotFound;
                RecomputeLiveUpdatesState();
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or GigagrugRequestException or TimeoutException or IOException or OperationCanceledException)
            {
                _logger.Warn(ex, $"Guild event stream dropped for {guildId}, retrying in {backoff}");
            }

            _isEventStreamLive = false;
            RecomputeLiveUpdatesState();
            try
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, EventStreamMaxBackoff.Ticks));
        }
    }

    private void UpdateAccessEventStream()
    {
        var shouldRun = IsSignedIn && IsApiReachable && !_accessEventsUnsupported;
        if (shouldRun == _accessEventsRunning)
        {
            RecomputeLiveUpdatesState();
            return;
        }

        _accessEventsCts?.Cancel();
        _accessEventsCts?.Dispose();
        _accessEventsCts = null;
        _accessEventsRunning = shouldRun;
        _isAccessEventStreamLive = false;
        if (!shouldRun)
        {
            RecomputeLiveUpdatesState();
            return;
        }

        _accessEventsCts = new CancellationTokenSource();
        _ = RunAccessEventStreamAsync(_accessEventsCts.Token);
        RecomputeLiveUpdatesState();
    }

    private async Task RunAccessEventStreamAsync(CancellationToken cancellationToken)
    {
        var backoff = EventStreamMinBackoff;
        _logger.Info("Access event stream connecting");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var eventType in _gigagrugClient.StreamAccessEventsAsync(cancellationToken).ConfigureAwait(true))
                {
                    if (backoff != EventStreamMinBackoff || !_isAccessEventStreamLive)
                    {
                        _logger.Info("Access event stream ready");
                    }

                    backoff = EventStreamMinBackoff;
                    _isAccessEventStreamLive = true;
                    RecomputeLiveUpdatesState();
                    if (eventType == "accessChanged")
                    {
                        QueueAccessRecheck();
                    }
                    else if (eventType == "bannersChanged")
                    {
                        _logger.Info("Access event stream: bannersChanged");
                        QueueBannerRefresh();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.Info("Access event stream stopped");
                return;
            }
            catch (SessionExpiredException)
            {
                _logger.Info("Access event stream dropped: 401, session expired");
                SignOutTo(GateFailure.SessionExpired);
                return;
            }
            catch (GigagrugRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Info($"Access event stream giving up, status={ex.StatusCode}");
                _accessEventsUnsupported = true;
                _isAccessEventStreamLive = false;
                RecomputeLiveUpdatesState();
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or GigagrugRequestException or TimeoutException or IOException or OperationCanceledException)
            {
                _logger.Warn(ex, $"Access event stream dropped, retrying in {backoff}");
            }

            _isAccessEventStreamLive = false;
            RecomputeLiveUpdatesState();
            try
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, EventStreamMaxBackoff.Ticks));
        }
    }

    private void RecomputeLiveUpdatesState()
    {
        if (!IsSignedIn || !IsApiReachable)
        {
            LiveUpdatesState = LiveUpdatesState.Hidden;
            return;
        }

        var guildOk = !HasStewardFeature || _eventsUnsupported || _isEventStreamLive;
        var accessOk = _accessEventsUnsupported || _isAccessEventStreamLive;
        LiveUpdatesState = guildOk && accessOk ? LiveUpdatesState.Live : LiveUpdatesState.Reconnecting;
    }

    private void QueueAccessRecheck()
    {
        _accessRecheckPending = true;
        if (!_isAccessRechecking)
        {
            _ = RunAccessRecheckAsync();
        }
    }

    private async Task RunAccessRecheckAsync()
    {
        _isAccessRechecking = true;
        try
        {
            while (_accessRecheckPending)
            {
                await Task.Delay(AccessEventDebounce).ConfigureAwait(true);
                _accessRecheckPending = false;
                if (!_isChecking)
                {
                    await RecheckAuthorizationAsync(CancellationToken.None).ConfigureAwait(true);
                }
            }
        }
        finally
        {
            _isAccessRechecking = false;
        }
    }

    private void QueueBannerRefresh()
    {
        _bannerRefreshPending = true;
        if (!_isBannerRefreshing)
        {
            _ = RunBannerRefreshAsync();
        }
    }

    private async Task RunBannerRefreshAsync()
    {
        _isBannerRefreshing = true;
        try
        {
            while (_bannerRefreshPending)
            {
                await Task.Delay(AccessEventDebounce).ConfigureAwait(true);
                _bannerRefreshPending = false;
                _lastBannersSync = DateTimeOffset.Now;
                await SyncBannersAsync().ConfigureAwait(true);
                _logger.Info("Banners refetched from bannersChanged event");
            }
        }
        finally
        {
            _isBannerRefreshing = false;
        }
    }

    private void QueueGuildPull()
    {
        _guildPullPending = true;
        if (!_isGuildPulling)
        {
            _ = RunGuildPullAsync();
        }
    }

    private async Task RunGuildPullAsync()
    {
        _isGuildPulling = true;
        try
        {
            while (_guildPullPending)
            {
                await Task.Delay(GuildEventDebounce).ConfigureAwait(true);
                _guildPullPending = false;
                await SyncRosterAsync().ConfigureAwait(true);
                await NotifySavedVariablesChangedAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            _isGuildPulling = false;
        }
    }

    public async Task SavedVariablesWrittenAsync()
    {
        await PushCharacterSyncAsync().ConfigureAwait(true);
        await NotifySavedVariablesChangedAsync().ConfigureAwait(true);
    }

    public bool IsRosterInSync(string flavourPath, string addOnsPath, string? charactersFingerprint)
    {
        if (charactersFingerprint is null || _guildId is not { } guildId)
        {
            return false;
        }

        var state = _stateStore.Load();
        var key = AppStateStore.CharacterSyncKey(guildId, flavourPath);
        var pushedUp = !CharacterPushGate.ShouldPush(charactersFingerprint, state.CharacterSync, key)
            && state.CharacterSync.GetValueOrDefault(key)?.Error is null;

        return pushedUp && IsGuildDataWritten(flavourPath, addOnsPath);
    }

    public bool IsGuildDataWritten(string flavourPath, string addOnsPath) =>
        _stateStore.Load().GuildRosterSync.TryGetValue(flavourPath, out var written)
        && written == StewardSyncFile.ReadFingerprint(addOnsPath);

    public IReadOnlyDictionary<string, CharacterPushOutcome> GetCharacterOutcomes(string flavourPath)
    {
        if (_guildId is not { } guildId)
        {
            return new Dictionary<string, CharacterPushOutcome>();
        }

        var key = AppStateStore.CharacterSyncKey(guildId, flavourPath);
        return _stateStore.Load().CharacterSync.GetValueOrDefault(key)?.Characters ?? new Dictionary<string, CharacterPushOutcome>();
    }

    public string DescribeRejection(string characterGuid, string reason) =>
        reason == CharacterSyncRejectionCopy.NotLinkedReason
            ? CharacterSyncRejectionCopy.DescribeNotLinked(
                characterGuid,
                HasRosterFeature ? _lastDirectory?.Characters : null,
                _lastDirectory?.People,
                _userId)
            : CharacterSyncRejectionCopy.Describe(reason);

    public async Task PushCharacterSyncAsync(bool force = false)
    {
        _pendingPush = force || _pendingPush == true;
        if (_isPushing)
        {
            return;
        }

        _isPushing = true;
        try
        {
            while (_pendingPush is { } next)
            {
                _pendingPush = null;
                await PushCharacterSyncOnceAsync(next).ConfigureAwait(true);
            }
        }
        finally
        {
            _isPushing = false;
        }
    }

    private async Task PushCharacterSyncOnceAsync(bool force)
    {
        if (!HasSyncFeature)
        {
            return;
        }

        if (force && !await EnsureAuthorizedForActionAsync(CancellationToken.None).ConfigureAwait(true))
        {
            return;
        }

        if (!HasSyncFeature || _guildId is not { } guildId)
        {
            return;
        }

        foreach (var install in Installs.ToList())
        {
            if (await PushCharacterSyncAsync(install, guildId, force).ConfigureAwait(true))
            {
                return;
            }
        }
    }

    private async Task<bool> PushCharacterSyncAsync(WowInstallViewModel install, string guildId, bool force)
    {
        var key = AppStateStore.CharacterSyncKey(guildId, install.FlavourPath);
        SavedVariablesSnapshot? snapshot;
        try
        {
            snapshot = await Task.Run(() => StewardSavedVariables.Read(install.FlavourPath)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetCharacterSyncRow(install, key, null, [], $"Could not read the Steward saved variables: {ex.Message}");
            return false;
        }

        var professionsOnly = IsProfessionsOnlySync;
        IReadOnlyList<CharacterObservation> characters = snapshot is null ? []
            : professionsOnly ? CharacterSyncMapping.FilterToProfessionsOnly(snapshot)
            : snapshot.Characters;
        var fingerprint = snapshot is not { HasAccountData: true } ? null
            : professionsOnly ? CharacterSyncMapping.Fingerprint(characters, snapshot.Professions, snapshot.Catalogue)
            : snapshot.CharactersFingerprint;
        var state = _stateStore.Load();
        if (fingerprint is null)
        {
            if (force)
            {
                SetCharacterSyncRow(install, key, null, [], "No Steward saved variables with characters found for this install.");
            }

            return false;
        }

        var last = state.CharacterSync.GetValueOrDefault(key);
        var plan = professionsOnly
            ? ProfessionsPushSelection.Select(characters, snapshot!.Professions, snapshot.Catalogue, last, _lastDirectory?.Characters, _userId, force)
            : null;
        var gateOpen = plan is null
            ? CharacterPushGate.ShouldPush(fingerprint, state.CharacterSync, key)
            : plan.HasWork && !(last is { Error: not null } && last.Fingerprint == fingerprint);
        if ((!force && !gateOpen) || !HasSyncFeature)
        {
            return false;
        }

        if (force)
        {
            state.CharacterSyncBatches.Remove(key);
        }

        var sentCharacters = plan?.Characters ?? characters;
        var sentCatalogue = plan is { SendCatalogue: false } || snapshot!.Catalogue.Count == 0 ? null : snapshot.Catalogue;
        var batchFingerprint = plan is null ? fingerprint! : CharacterSyncMapping.Fingerprint(sentCharacters, snapshot!.Professions, sentCatalogue);
        var batchId = ResolveBatchId(state, key, batchFingerprint);
        var request = new CharacterSyncRequest(
            batchId,
            InstalledVersion,
            [.. sentCharacters.Select(c => CharacterSyncMapping.ToEntry(c, snapshot!.Professions))],
            sentCatalogue,
            professionsOnly || snapshot!.GuildRanks is null ? null : CharacterSyncMapping.ToSync(snapshot.GuildRanks));

        try
        {
            var result = await _gigagrugClient.PostCharacterSyncAsync(guildId, request, CancellationToken.None).ConfigureAwait(true);
            // gigagrug answers a replayed batchId with accepted 0 and no rejections, which says nothing about each character.
            if (plan is not null && result.Replay)
            {
                state = _stateStore.Load();
                state.CharacterSyncBatches.Remove(key);
                _stateStore.Save(state);
                return false;
            }

            var rejections = result.Rejected.ToDictionary(r => r.CharacterGuid, r => r.Reason, StringComparer.Ordinal);
            var outcomes = plan is not null
                ? ProfessionsPushSelection.Merge(last?.Characters, plan, rejections)
                : characters.ToDictionary(
                    c => c.CharacterGuid,
                    c => rejections.TryGetValue(c.CharacterGuid, out var reason)
                        ? new CharacterPushOutcome(false, reason)
                        : new CharacterPushOutcome(true),
                    StringComparer.Ordinal);
            var catalogueFingerprint = plan is null ? null : plan.SendCatalogue ? plan.CatalogueFingerprint : last?.CatalogueFingerprint;
            state = _stateStore.Load();
            state.CharacterSync[key] = new CharacterPushRecord(fingerprint!, DateTimeOffset.Now, result.Accepted, Characters: outcomes, CatalogueFingerprint: catalogueFingerprint);
            _stateStore.Save(state);
            SetCharacterSyncRow(install, key, result.Accepted, result.Rejected, null);
            _logger.Info(
                $"Character push for {guildId} {install.FlavourPath}: {result.Accepted} accepted, {result.Rejected.Count} rejected");
            _lastDirectorySync = DateTimeOffset.Now;
            await SyncDirectoryAsync().ConfigureAwait(true);
        }
        catch (SessionExpiredException)
        {
            _logger.Info($"Character push for {guildId} {install.FlavourPath}: 401, session expired");
            SignOutTo(GateFailure.SessionExpired);
            return true;
        }
        catch (GigagrugRequestException ex)
        {
            _logger.Warn(ex, $"Character push for {guildId} {install.FlavourPath} rejected, status={ex.StatusCode}");
            var message = ex.StatusCode == HttpStatusCode.Forbidden
                ? "Your account can't sync this guild."
                : ex.Body ?? ex.Message;
            state = _stateStore.Load();
            state.CharacterSync[key] = plan is null
                ? new CharacterPushRecord(fingerprint!, DateTimeOffset.Now, 0, message)
                : new CharacterPushRecord(fingerprint!, DateTimeOffset.Now, 0, message, last?.Characters, last?.CatalogueFingerprint);
            _stateStore.Save(state);
            SetCharacterSyncRow(install, key, null, [], message);
        }
        catch (GigagrugThrottledException)
        {
            _logger.Info($"Character push for {guildId} {install.FlavourPath}: 429, throttled");
            SetCharacterSyncRow(install, key, null, [], "Sent too recently, trying again shortly");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.Warn(ex, $"Character push for {guildId} {install.FlavourPath} failed, gigagrug unreachable");
            SetCharacterSyncRow(install, key, null, [], "gigagrug unreachable, retrying");
        }

        return false;
    }

    private string ResolveBatchId(AppState state, string key, string fingerprint)
    {
        var pending = state.CharacterSyncBatches.GetValueOrDefault(key);
        var batchId = CharacterPushGate.ResolveBatchId(pending, fingerprint, Guid.NewGuid().ToString());
        state.CharacterSyncBatches[key] = new CharacterSyncBatch(fingerprint, batchId);
        _stateStore.Save(state);
        return batchId;
    }

    private void SetCharacterSyncRow(WowInstallViewModel install, string key, int? accepted, IReadOnlyList<CharacterSyncRejection> rejected, string? error)
    {
        var pushedAt = accepted is null
            ? _stateStore.Load().CharacterSync.GetValueOrDefault(key)?.PushedAt
            : DateTimeOffset.Now;

        var existing = CharacterSyncRows.FirstOrDefault(row => row.FlavourPath == install.FlavourPath);
        var row = new CharacterSyncRowViewModel(install.DisplayName, install.FlavourPath, pushedAt, accepted, error, rejected);
        if (existing is null)
        {
            CharacterSyncRows.Add(row);
        }
        else
        {
            CharacterSyncRows[CharacterSyncRows.IndexOf(existing)] = row;
        }
    }

    private void RefreshClients()
    {
        foreach (var install in Installs)
        {
            install.RefreshClientRunning();
        }

        RestedXp.SetInstalls(Installs);
    }

    private async Task AutoApplyAsync()
    {
        if (_isAutoApplying || IsSigningIn || !IsSignedIn)
        {
            return;
        }

        var state = _stateStore.Load();
        var mode = AppStateStore.ParseAutoUpdate(state.AutoUpdate);
        if (mode == AutoUpdateMode.Never)
        {
            return;
        }

        _isAutoApplying = true;
        try
        {
            foreach (var install in Installs.ToList())
            {
                if (mode == AutoUpdateMode.OutOfGame && install.IsClientRunning)
                {
                    continue;
                }

                foreach (var row in install.AddonRows.ToList())
                {
                    if (row.CanAutoApply && !AppStateStore.IsExcludedFromUpdates(state, install.FlavourPath, row.AddonId))
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
                channel.Apply(status);
            }
        }
    }

    private void ReconcileFeatureGating()
    {
        var visible = VisibleAddons();
        var visibleIds = visible.Select(addon => addon.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _status.Keys.Where(id => !visibleIds.Contains(id)).ToList())
        {
            _status.Remove(id);
        }

        foreach (var id in _releases.Keys.Where(id => !visibleIds.Contains(id)).ToList())
        {
            _releases.Remove(id);
        }

        foreach (var install in Installs)
        {
            install.SyncAddons(visible);
        }

        RebuildAddonChannels();
        SyncRestedXpRows();
        SyncCharacterSyncRows();
    }

    private void SyncCharacterSyncRows()
    {
        if (!HasSyncFeature)
        {
            CharacterSyncRows.Clear();
            return;
        }

        var known = new HashSet<string>(Installs.Select(install => install.FlavourPath), StringComparer.OrdinalIgnoreCase);
        foreach (var gone in CharacterSyncRows.Where(row => !known.Contains(row.FlavourPath)).ToList())
        {
            CharacterSyncRows.Remove(gone);
        }

        var state = _stateStore.Load();
        foreach (var install in Installs)
        {
            var last = _guildId is { } guildId
                ? state.CharacterSync.GetValueOrDefault(AppStateStore.CharacterSyncKey(guildId, install.FlavourPath))
                : null;
            var accepted = last?.Error is null ? last?.Accepted : null;
            var existing = CharacterSyncRows.FirstOrDefault(r => r.FlavourPath == install.FlavourPath);
            if (existing is not null && _characterRowsGuildId == _guildId)
            {
                continue;
            }

            var row = new CharacterSyncRowViewModel(install.DisplayName, install.FlavourPath, last?.PushedAt, accepted, last?.Error, []);
            if (existing is null)
            {
                CharacterSyncRows.Add(row);
            }
            else
            {
                CharacterSyncRows[CharacterSyncRows.IndexOf(existing)] = row;
            }
        }

        _characterRowsGuildId = _guildId;
    }

    private void RebuildAddonChannels()
    {
        var visibleIds = VisibleAddons().Select(addon => addon.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in AddonChannels.Where(channel => !visibleIds.Contains(channel.AddonId)).ToList())
        {
            AddonChannels.Remove(gone);
        }

        foreach (var addon in VisibleAddons())
        {
            if (!AddonChannels.Any(channel => string.Equals(channel.AddonId, addon.Id, StringComparison.OrdinalIgnoreCase)))
            {
                AddonChannels.Add(new AddonChannelViewModel(addon, _stateStore, OnChannelChanged));
            }
        }

        ApplyChannelStatus();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isLoadingState)
        {
            return;
        }

        _stateStore.Save(_stateStore.Load() with { MinimizeToTray = value });
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_isLoadingState)
        {
            return;
        }

        _stateStore.Save(_stateStore.Load() with { CloseToTray = value });
    }

    partial void OnAutoUpdateIndexChanged(int value)
    {
        if (_isLoadingState)
        {
            return;
        }

        var auto = value switch
        {
            0 => "always",
            2 => "never",
            _ => "out-of-game",
        };
        _stateStore.Save(_stateStore.Load() with { AutoUpdate = auto });
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isLoadingState)
        {
            return;
        }

        if (App.IsPackaged)
        {
            _ = SetStartupTaskAsync(value);
            return;
        }

        if (!App.IsGitHubRelease)
        {
            return;
        }

        StartupRegistration.Set(value);
    }

    partial void OnAvatarUriChanged(Uri? value) => _avatarImage = value is null ? null : new BitmapImage(value);

    private void OnChannelChanged(string addonId, string channel)
    {
        if (!_releases.TryGetValue(addonId, out var releases))
        {
            return;
        }

        var addon = _addons.First(a => string.Equals(a.Id, addonId, StringComparison.OrdinalIgnoreCase));
        _status[addonId] = AddonChannelStatus.Resolve(channel, releases, addon.Channels, addon.DefaultPreference);
        ApplyStatus(background: false);
        RecomputeSummary();
    }

    private WowInstallViewModel AddInstall(WowInstall install, bool isAddedByUser)
    {
        var viewModel = new WowInstallViewModel(
            install,
            install.ProductCode is { } product && _supportedProducts.TryGetValue(product, out var productName) ? productName : null,
            _stateStore.Load().InstallLabels.GetValueOrDefault(install.FlavourPath),
            VisibleAddons(),
            [.. _addons.Select(addon => addon.FolderName), StewardGuidesAddon.FolderName],
            _addonUpdater,
            _stateStore,
            EnsureAuthorizedForActionAsync,
            _features.Contains,
            ShowChannelDialogFor,
            ConfirmUninstallAsync,
            RemoveInstall,
            OnClientExited,
            wowInstall => AfterStewardInstalled?.Invoke(wowInstall) ?? Task.CompletedTask,
            _logger)
        {
            IsAddedByUser = isAddedByUser,
        };
        viewModel.SetIsAdmin(IsAuthorized);
        viewModel.RowsChanged += OnInstallRowsChanged;
        Installs.Add(viewModel);
        SyncGuideInstalls();
        SyncCharacterSyncRows();
        return viewModel;
    }

    private void OnInstallRowsChanged(object? sender, EventArgs e)
    {
        var hidden = _stateStore.Load().HiddenAddons.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var install in Installs)
        {
            install.SyncHidden(hidden);
        }

        RecomputeSummary();
    }

    private void OnClientExited(WowInstallViewModel install)
    {
        RestedXp.Confirm(install.FlavourPath);
        _ = NotifySavedVariablesChangedAsync();
        _ = AutoApplyAsync();
    }

    private Task NotifySavedVariablesChangedAsync() => SavedVariablesChanged?.Invoke() ?? Task.CompletedTask;

    private void RecomputeSummary()
    {
        foreach (var name in SummaryNames)
        {
            OnPropertyChanged(name);
        }

        RecomputeTable();
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
        if (!_isChecking)
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
            UpdateStoreAppInstalledState();

            var result = await RecheckAuthorizationAsync(CancellationToken.None).ConfigureAwait(true);
            if (result == AuthCheckResult.Authorized)
            {
                var guildSyncDue = !_isEventStreamLive || DateTimeOffset.Now - _lastGuildSync >= GuildSyncFallbackInterval;
                await CheckAsync(background: true, CancellationToken.None, guildSyncDue).ConfigureAwait(true);
                if (guildSyncDue)
                {
                    _lastGuildSync = DateTimeOffset.Now;
                    await PushCharacterSyncAsync().ConfigureAwait(true);
                }
                if (DateTimeOffset.Now - _lastDirectorySync >= GuildSyncFallbackInterval)
                {
                    _lastDirectorySync = DateTimeOffset.Now;
                    await SyncDirectoryAsync().ConfigureAwait(true);
                }
                if (DateTimeOffset.Now - _lastBannersSync >= GuildSyncFallbackInterval)
                {
                    _lastBannersSync = DateTimeOffset.Now;
                    await SyncBannersAsync().ConfigureAwait(true);
                }
                if (!App.IsPackaged || DateTimeOffset.Now - _lastStoreCheck >= StoreCheckInterval)
                {
                    await CheckAppUpdateAsync().ConfigureAwait(true);
                }
            }

            await RestedXp.RefreshSessionAsync().ConfigureAwait(true);
            if (DateTimeOffset.Now - _lastGuideCheck >= _nextGuideCheckDue)
            {
                await CheckGuidesAsync().ConfigureAwait(true);
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
            var features = GigagrugClient.EffectiveFeatures(me);
            if (!GigagrugClient.IsAuthorizing(features))
            {
                _logger.Info($"/api/me recheck: not authorized, {features.Count} feature(s) held");
                SignOutTo(GateFailure.NotAuthorized);
                return AuthCheckResult.NotAuthorized;
            }

            var guild = me.ResolveGuild(_stateStore.Load().GuildId);
            _guildId = guild?.Id;
            _meGuilds = me.Guilds;

            var previousFeatures = new HashSet<string>(_features, StringComparer.Ordinal);
            var previousRole = Role;
            var previousUserId = _userId;
            _features.Clear();
            _features.UnionWith(features);
            UpdateGuildFeatures();
            if (!previousFeatures.SetEquals(_features))
            {
                ReconcileFeatureGating();
            }

            UserName = me.User.Name;
            UserHandle = me.User.Username is { Length: > 0 } u ? $"@{u}" : null;
            Role = me.User.Role;
            _userId = me.User.Id;
            AvatarUri = Uri.TryCreate(me.User.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null;
            IsAuthorized = true;
            SetGuilds(me.Guilds, guild);
            StatusMessage = null;
            Failure = GateFailure.None;

            // Client-side gate only: gigagrug does not restrict who can fetch the unstable manifest.
            IsGlobalAdmin = GigagrugClient.IsGlobalAdmin(me);

            if (!HasStewardFeature
                && (!previousFeatures.SetEquals(_features) || previousRole != Role || previousUserId != _userId))
            {
                WriteMe(Installs.Select(install => install.Install), new SyncMe(_userId, Role, [.. _features]));
            }

            PropagateAuthorized();
            UpdateEventStream();
            UpdateAccessEventStream();
            _logger.Info($"/api/me recheck: authorized, role={Role}, {_features.Count} feature(s)");
            return AuthCheckResult.Authorized;
        }
        catch (SessionExpiredException)
        {
            _logger.Info("/api/me recheck: 401, session expired");
            SignOutTo(GateFailure.SessionExpired);
            return AuthCheckResult.SessionExpired;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.Warn(ex, "/api/me recheck: gigagrug unreachable");
            Failure = GateFailure.Unreachable;
            StatusMessage = ex.Message;
            UpdateEventStream();
            UpdateAccessEventStream();
            return AuthCheckResult.Unreachable;
        }
    }

    private void SignOutTo(GateFailure failure)
    {
        var wasOfficer = HasStewardFeature;
        var installs = Installs.Select(install => install.Install).ToList();
        var lastOfficerPayload = _lastOfficerPayload;
        _sessionService.ClearSession();
        ResetInstalls();
        IsAuthorized = false;
        IsGlobalAdmin = false;
        UserName = null;
        UserHandle = null;
        Role = null;
        _userId = null;
        AvatarUri = null;
        _guildId = null;
        SetGuilds([], null);
        StatusMessage = null;
        IsSignedIn = false;
        Failure = failure;
        if (wasOfficer)
        {
            // An officer's file carries the full roster payload; rewriting it minus me keeps that data instead of wiping it with a roster-less skeleton.
            if (lastOfficerPayload is { Me: not null })
            {
                var cleared = lastOfficerPayload with { Me = null };
                foreach (var install in installs)
                {
                    GuildRosterSync.WriteIfChanged(install, cleared, _stateStore, logger: _logger);
                }
            }
        }
        else
        {
            WriteMe(installs, null);
        }

        PropagateAuthorized();
        UpdateEventStream();
        UpdateAccessEventStream();
    }

    private void WriteMe(IEnumerable<WowInstall> installs, SyncMe? me, bool force = false)
    {
        var catalogue = me is null ? null : _lastMemberCatalogue;
        var payload = new SyncPayload(DateTimeOffset.Now, null, [], [], [], [], [], [])
        {
            Me = me,
            Directory = me is null ? null : _lastDirectory,
            Catalogue = catalogue is null
                ? new Dictionary<string, IReadOnlyList<CatalogueRecipe>>()
                : MemberCatalogueMapping.ToCatalogue(catalogue),
        };
        foreach (var install in installs)
        {
            GuildRosterSync.WriteIfChanged(install, payload, _stateStore, force, _logger);
        }
    }

    private void ResetInstalls()
    {
        _recheckTimer?.Stop();
        DisposeInstalls();
        _status.Clear();
        _releases.Clear();
        _features.Clear();
        _guildFeatures.Clear();
        _meGuilds = [];
        _lastDirectory = null;
        _lastMemberCatalogue = null;
        _lastDirectorySync = default;
        _lastOfficerPayload = null;
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
        Unreachable,
    }
}
