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
    ClientOutdated,
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
        nameof(SelectedGuide),
        nameof(SelectedGuideVisibility),
        nameof(GuidesNotInstalledVisibility),
        nameof(GuidesNotInstalledText),
        nameof(TitleBarPickerVisibility),
        nameof(SyncVisibility),
        nameof(HasSyncFeature),
        nameof(CanPushCharacters),
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
    private readonly IReadOnlyDictionary<string, int> _curseForgeVersionTypes;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dictionary<string, AddonChannelStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, AddonRelease?>> _releases =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _features = new(StringComparer.Ordinal);
    private readonly HashSet<string> _guildFeatures = new(StringComparer.Ordinal);
    private IReadOnlyList<AdminGuild> _meGuilds = [];
    private IReadOnlyList<ManagedAddon>? _addonCatalogue;

    private DispatcherQueueTimer? _recheckTimer;
    private CancellationTokenSource? _signInCts;
    private string? _guildId;
    private string? _userId;
    private string? _characterRowsGuildId;
    private DateTimeOffset _lastPass;
    private DateTimeOffset _lastGuideCheck;
    private TimeSpan _nextGuideCheckDue = GuideCheckInterval;
    private readonly double _intervalJitter = 1 + Random.Shared.NextDouble() / 5;
    private DateTimeOffset _lastAuthCheck;
    private DateTimeOffset _lastStoreCheck;
    private StoreContext? _storeContext;
    private IReadOnlyList<StorePackageUpdate>? _storeUpdates;
    private Task? _storeInstall;
    private StoreQueueItem? _storeQueueItem;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Lock _progressGate = new();
    private AppUpdatePhase _reportedPhase;
    private long _reportedAt;
    private int _progressGeneration;
    private DateTimeOffset _lastGuildSync;
    private DateTimeOffset _lastDirectorySync;
    private DateTimeOffset _lastBannersSync;
    private DateTimeOffset _clientOutdatedAt;
    private IReadOnlyList<Banner> _allBanners = [];
    private readonly List<BannerViewModel> _localBanners = [];
    private SyncDirectory? _lastDirectory;
    private IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>>? _lastMemberCatalogue;
    private SyncPayload? _lastOfficerPayload;
    private string? _lastOfficerPayloadGuild;
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
    private Task<AuthCheckResult>? _actionAuthorization;
    private bool _isLoadingState;
    private bool _isChoosingGuild;
    private bool _guildPromptDeferred;
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
        IReadOnlyDictionary<string, int> curseForgeVersionTypes,
        RestedXpService restedXpService,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(addons);
        ArgumentNullException.ThrowIfNull(supportedProducts);

        _curseForgeVersionTypes = curseForgeVersionTypes;

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
        Installs.CollectionChanged += (_, _) => RenumberInstalls();

        var state = stateStore.Load();
        _addonCatalogue = state.AddonCatalogue?.Select(addon => addon.ToManagedAddon()).ToList();
        _isLoadingState = true;
        MinimizeToTray = state.MinimizeToTray;
        CloseToTray = state.CloseToTray;
        CurseForgeEnabled = state.CurseForgeEnabled;
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
        RestedXp.Guides.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedGuide));
            OnPropertyChanged(nameof(SelectedGuideVisibility));
            OnPropertyChanged(nameof(GuidesNotInstalledVisibility));
        };

        Sync = new SyncViewModel(this)
        {
            StateChanged = () => OnPropertyChanged(nameof(SyncBadgeVisibility)),
        };
        SavedVariablesChanged = Sync.ReloadAsync;
        AfterStewardInstalled = install => HasStewardFeature ? Sync.WriteGeneratedFileAsync(install) : WriteMeAfterInstallAsync();
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
    [NotifyPropertyChangedFor(nameof(AccountAutomationName))]
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
    [NotifyPropertyChangedFor(nameof(CheckedText), nameof(CheckedStaleVisibility))]
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
    [NotifyPropertyChangedFor(nameof(TimeoutVisibility), nameof(SessionExpiredVisibility), nameof(UnreachableVisibility), nameof(NotAuthorizedVisibility), nameof(SignInFailedVisibility), nameof(IsApiReachable), nameof(StatusActionVisibility), nameof(StatusActionLabel), nameof(ClientOutdatedVisibility), nameof(UpdateAllEnabled))]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand), nameof(UpdateAllInstallsCommand))]
    public partial GateFailure Failure { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurseForgeEnabled))]
    public partial bool CurseForgeEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveUpdatesLiveVisibility), nameof(LiveUpdatesReconnectingVisibility), nameof(LiveUpdatesTooltip), nameof(AccountAutomationName))]
    public partial LiveUpdatesState LiveUpdatesState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleBarPickerVisibility))]
    public partial bool IsSettingsShown { get; set; }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppUpdateVisibility), nameof(AppUpdateIdleVisibility), nameof(AppUpdateProgressVisibility), nameof(IsAppUpdateInProgress), nameof(AppUpdateActionLabel), nameof(AboutDescription), nameof(AboutActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(InstallAppUpdateCommand), nameof(CheckOrInstallAppUpdateCommand))]
    public partial StoreUpdateProgress AppUpdateProgress { get; set; } = StoreUpdateProgress.None;

    public bool IsAppUpdateInProgress => AppUpdateProgress.IsActive;

    public Visibility AppUpdateVisibility => When(AppUpdate is not null || IsAppUpdateInProgress);

    public Visibility AppUpdateIdleVisibility => When(!IsAppUpdateInProgress);

    public Visibility AppUpdateProgressVisibility => When(IsAppUpdateInProgress);

#pragma warning disable CA1822 // x:Bind resolves these through a ViewModel instance
    public string AppUpdateTitle => "A Steward update is available in the Microsoft Store";
#pragma warning restore CA1822

    public string AppUpdateActionLabel => IsAppUpdateInProgress ? "Installing…" : "Install update";

    public Uri? AppUpdateChangelogUri => AppUpdate is null ? null
        : new Uri("https://github.com/hoobio/steward-companion/releases/latest");

    public Visibility StoreListingVisibility => When(IsStoreAppInstalled == false);

    public Visibility StoreSwitchVisibility => When(IsStoreAppInstalled == true);

#pragma warning disable CA1822 // x:Bind resolves these through a ViewModel instance
    public Visibility CheckForUpdatesVisibility => When(App.IsGitHubRelease);
#pragma warning restore CA1822

    public string AboutActionLabel => IsAppUpdateInProgress ? "Installing…" : IsCheckingAppUpdate ? "Checking" : AppUpdate is null ? "Check for a new version" : "Install update";

    private IReadOnlyList<string> VisibleChannels => IsGlobalAdmin ? AddonChannelStatus.Ordered : ["release", "pre-release"];

    public bool HasGuidesFeature => _features.Contains(GigagrugClient.GuidesFeature);

    public bool HasAddonsFeature => _features.Contains(GigagrugClient.AddonsFeature);

    public bool IsCurseForgeEnabled => CurseForgeEnabled && HasAddonsFeature;

    public Visibility CurseForgeSettingVisibility => When(HasAddonsFeature);

    public bool HasStewardFeature => _guildFeatures.Contains(GigagrugClient.StewardFeature);

    public bool HasSyncFeature => _guildFeatures.Contains(GigagrugClient.SyncFeature);

    public bool HasRosterFeature => _guildFeatures.Contains(GigagrugClient.RosterFeature);

    public bool HasProfessionsFeature => _guildFeatures.Contains(GigagrugClient.ProfessionsFeature);

    public SyncDirectory? LastDirectory => _lastDirectory;

    public IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>>? LastMemberCatalogue => _lastMemberCatalogue;

    public SyncPayload? LastOfficerPayload => _lastOfficerPayload;

    public string? UserId => _userId;

    public bool IsProfessionsOnlySync => HasSyncFeature && !HasStewardFeature;

    private IReadOnlyList<ManagedAddon> VisibleAddons() =>
        [.. AddonCatalogue.Visible(_addonCatalogue, _addons, _features).Where(addon => IsCurseForgeEnabled || addon.Source != CurseForgeAddons.Source)];

    private bool IsAdminFor(string addonId) =>
        Failure != GateFailure.ClientOutdated
        && (IsAuthorized || (IsSignedIn && Failure == GateFailure.Unreachable && AddonCatalogue.UpdatesWhileUnreachable(_addonCatalogue, addonId)));

    private bool SetAddonCatalogue(IReadOnlyList<CatalogueAddon>? catalogue)
    {
        _addonCatalogue = catalogue?.Select(addon => addon.ToManagedAddon()).ToList();
        var state = _stateStore.Load();
        if (AddonCatalogue.Same(state.AddonCatalogue, catalogue))
        {
            return false;
        }

        _stateStore.Save(state with { AddonCatalogue = catalogue?.ToList() });
        return true;
    }

    public int InstallCount => Installs.Count;

    public bool IsAnyRowBusy => Installs.Any(install => install.AddonRows.Any(row => row.IsBusy));

    public Visibility NoInstallsVisibility => When(Installs.Count == 0 && !IsBusy);

    public bool StatusMessageIsOpen => !string.IsNullOrEmpty(StatusMessage);

    public Visibility GateVisibility => When(!IsSignedIn);

    public Visibility ShellChromeVisibility => When(IsSignedIn);

    public Visibility SyncBadgeVisibility => When(Sync.HasWaiting);

    public Visibility GuidesVisibility => When(IsGuidesPreview
        || (HasGuidesFeature && RestedXp.IsSignedIn && Installs.Any(HasRestedXp)));

    private static bool HasRestedXp(WowInstallViewModel install) => install.AddonRows.Any(row =>
        string.Equals(row.AddonId, RestedXpViewModel.AddonId, StringComparison.OrdinalIgnoreCase)
        && row.State is not (AddonRowState.Missing or AddonRowState.NoReleases));

    public RestedXpInstallViewModel? SelectedGuide => RestedXp.IsPreview
        ? RestedXp.Guides.FirstOrDefault()
        : SelectedInstall is { } install && HasRestedXp(install)
            ? RestedXp.Guides.FirstOrDefault(guide => string.Equals(guide.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase))
            : null;

    public Visibility SelectedGuideVisibility => When(SelectedGuide is not null);

    public Visibility GuidesNotInstalledVisibility => When(SelectedGuide is null && SelectedInstall is not null);

    public string GuidesNotInstalledText => $"RestedXP Guides is not installed on {SelectedInstall?.Label}.";

    public Visibility TitleBarPickerVisibility => When(SelectedInstall is not null && !IsSettingsShown);

    public Visibility SyncVisibility => When(HasStewardFeature || CanPushCharacters);

    public bool CanPushCharacters => CharacterPushTargets().Count > 0;

    public ObservableCollection<CharacterSyncRowViewModel> CharacterSyncRows { get; } = [];

    public ObservableCollection<BannerViewModel> Banners { get; } = [];

    public Visibility TimeoutVisibility => When(Failure == GateFailure.Timeout);

    public Visibility SessionExpiredVisibility => When(Failure == GateFailure.SessionExpired);

    public Visibility UnreachableVisibility => When(Failure == GateFailure.Unreachable);

    public Visibility NotAuthorizedVisibility => When(Failure == GateFailure.NotAuthorized);

    public Visibility SignInFailedVisibility => When(Failure == GateFailure.SignInFailed);

    public bool IsApiReachable => Failure != GateFailure.Unreachable;

    public Visibility LiveUpdatesLiveVisibility => When(LiveUpdatesState == LiveUpdatesState.Live);

    public Visibility LiveUpdatesReconnectingVisibility => When(LiveUpdatesState == LiveUpdatesState.Reconnecting);

    public string? LiveUpdatesTooltip => LiveUpdatesState switch
    {
        LiveUpdatesState.Reconnecting => "Reconnecting to api.hoobi.io; changes still arrive on the next check",
        LiveUpdatesState.Live => "Connected to api.hoobi.io via SSE. Changes to guild data arrive instantly.",
        _ => null,
    };

    public string? AccountAutomationName => LiveUpdatesTooltip is { } tooltip ? $"{UserName}, {tooltip}" : UserName;

    public Visibility StatusActionVisibility => When(HasApiRetry || Failure is GateFailure.Unreachable or GateFailure.ClientOutdated);

    public string StatusActionLabel => Failure == GateFailure.ClientOutdated ? "Update Steward" : "Retry";

    public Visibility ClientOutdatedVisibility => When(Failure == GateFailure.ClientOutdated);

    public Visibility CancelSignInVisibility => When(IsSigningIn);

    public Visibility PendingSignInVisibility => When(PendingSignInUrl is not null);

    public Visibility BrowserOpenFailedVisibility => When(BrowserOpenFailed);

    public ImageSource? AvatarImage => _avatarImage;

    public string GateHeading => IsSigningIn ? "Waiting for Discord" : "Sign in to Steward";

    public Visibility UserHandleVisibility => When(!string.IsNullOrEmpty(UserHandle));

    public Visibility GuildPickerVisibility => When(Guilds.Count > 0);

    public bool HasGuildChoice => Guilds.Count > 1;

    public Visibility GuildChevronVisibility => When(HasGuildChoice);

    public Func<IReadOnlyList<GuildOptionViewModel>, GuildOptionViewModel, Task<GuildOptionViewModel?>>? ChooseGuild { get; set; }

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
        _ when IsAppUpdateInProgress => AppUpdateProgress.Text,
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
        if (_storeQueueItem is not null)
        {
            _storeQueueItem.StatusChanged -= OnStoreQueueItemStatusChanged;
        }

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
        RestedXp.SetInstalls(PresentInstalls);
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
            SetAddonCatalogue(null);
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
        catch (ClientOutdatedException ex)
        {
            EnterClientOutdated(ex.Message);
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

            DetectMissingInstalls();
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
            ShowMissingInstallBanners();
            await CheckAsync(background: false, cancellationToken).ConfigureAwait(true);
            EnsureInGameIcons();
            await PushCharacterSyncAsync().ConfigureAwait(true);
            _lastDirectorySync = DateTimeOffset.Now;
            await SyncDirectoryAsync().ConfigureAwait(true);
            await CheckAppUpdateAsync(forceStoreScan: true).ConfigureAwait(true);
            await CheckGuidesAsync().ConfigureAwait(true);

            StartRecheckTimer();
        }
        catch (Exception ex)
        {
            ReportFailure(ex, "startup", async () =>
            {
                await RefreshAsync().ConfigureAwait(true);
                StartRecheckTimer();
                return true;
            });
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

    private void EnsureInGameIcons()
    {
        foreach (var row in Installs.SelectMany(install => install.AddonRows))
        {
            _ = row.EnsureInGameIconAsync();
        }
    }

    private void RemoveInstall(string flavourPath)
    {
        if (Installs.FirstOrDefault(install => string.Equals(install.FlavourPath, flavourPath, StringComparison.OrdinalIgnoreCase)) is { } installed)
        {
            DetachInstall(installed);
        }

        _stateStore.Save(AppStateStore.RemoveInstall(_stateStore.Load(), flavourPath));
        _missingInstalls.Remove(flavourPath);
        SyncCharacterSyncRows();
        EnsureSelection();
        ShowMissingInstallBanners();
        RecomputeSummary();
    }

    private void DetachInstall(WowInstallViewModel install)
    {
        Installs.Remove(install);
        install.RowsChanged -= OnInstallRowsChanged;
        install.Dispose();
        RebuildAddonChannels();
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

            ApplyMissingInstalls();
            foreach (var install in Installs)
            {
                _ = install.RescanLocalAsync();
            }

            await CheckAsync(background: false, CancellationToken.None).ConfigureAwait(true);
            EnsureInGameIcons();
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

    private async Task CheckAppUpdateAsync(bool forceStoreScan = false, bool scanAllApps = false)
    {
        if (forceStoreScan && (!App.IsGitHubRelease || App.IsPackaged))
        {
            await ForceStoreScanAsync(scanAllApps).ConfigureAwait(true);
        }

        AppUpdate = App.IsPackaged && App.IsGitHubRelease
            ? await CheckStoreUpdateAsync().ConfigureAwait(true)
            : null;
    }

    private async Task ForceStoreScanAsync(bool scanAllApps = false)
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

            if (scanAllApps)
            {
                await ScanAllAppsForUpdatesAsync(manager).ConfigureAwait(true);
            }

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

    private async Task ScanAllAppsForUpdatesAsync(AppInstallManager manager)
    {
        try
        {
            // Only SearchForAllUpdatesAsync refreshes a flight's catalogue (verified 29 Sep 2026); it also queues every other Store app's update, hence explicit-press only.
            await manager.SearchForAllUpdatesAsync();
        }
        catch (Exception ex)
        {
            // Verified 29 Sep 2026: this call throws (AggregateException, cause uncaptured) yet still queues the update.
            _logger.Warn(ex, "Store scan for all apps failed");
        }

        var queued = manager.AppInstallItems.Any(queueItem => queueItem.PackageFamilyName == App.PackageFamilyName);
        _logger.Info($"Store scan for all apps: {manager.AppInstallItems.Count} item(s), Steward {(queued ? "queued" : "not queued")}");
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

            if (_storeInstall is not { IsCompleted: false })
            {
                _ = WatchStoreQueueAsync(context);
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
            SetStoreProgress(StoreUpdateProgress.From(AppUpdatePhase.Pending, 0, 0, 0));
            var operation = context.TrySilentDownloadAndInstallStorePackageUpdatesAsync(updates);
            operation.Progress = (_, status) => ReportStoreProgress(status);
            var result = await operation;
            FinishStoreProgress(result.OverallState.ToString(), result.OverallState is StorePackageUpdateState.Completed);
            if (result.OverallState is not (StorePackageUpdateState.Completed or StorePackageUpdateState.Canceled))
            {
                StatusMessage = $"Could not install the Microsoft Store update: {result.OverallState}";
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Store silent update install failed");
            FinishStoreProgress("with an error", completed: false);
            StatusMessage = $"Could not install the Microsoft Store update: {ex.Message}";
        }
    }

    private async Task WatchStoreQueueAsync(StoreContext context)
    {
        // The finished install's queue item outlives the restart and read as installing forever (29 Sep 2026).
        if (_storeQueueItem is not null || _storeUpdates is not { Count: > 0 })
        {
            return;
        }

        try
        {
            var items = await context.GetAssociatedStoreQueueItemsAsync();
            if (_storeQueueItem is not null || items.FirstOrDefault(item => item.InstallKind == StoreQueueItemKind.Update) is not { } item)
            {
                return;
            }

            _storeQueueItem = item;
            item.StatusChanged += OnStoreQueueItemStatusChanged;
            _logger.Info("Store update: following an update the Microsoft Store queued");
            OnStoreQueueItemStatusChanged(item, null);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Store update queue check failed");
        }
    }

    private void OnStoreQueueItemStatusChanged(StoreQueueItem item, object? args)
    {
        var status = item.GetCurrentStatus();
        var waiting = status.PackageInstallState is StoreQueueItemState.Paused
            || status.PackageInstallState is StoreQueueItemState.Active && status.UpdateStatus.PackageUpdateState is StorePackageUpdateState.Pending;
        // The Store kept a Pending Update item for Steward with no update available to StoreContext (29 Sep 2026).
        if (waiting && _storeUpdates is not { Count: > 0 })
        {
            return;
        }

        switch (status.PackageInstallState)
        {
            case StoreQueueItemState.Active:
                ReportStoreProgress(status.UpdateStatus);
                return;
            case StoreQueueItemState.Paused:
                ReportStoreProgress(StoreUpdateProgress.From(AppUpdatePhase.Pending, 0, 0, 0));
                return;
        }

        item.StatusChanged -= OnStoreQueueItemStatusChanged;
        RunOnUi(() =>
        {
            _storeQueueItem = null;
            FinishStoreProgress(status.PackageInstallExtendedState.ToString(), status.PackageInstallState is StoreQueueItemState.Completed);
            if (status.PackageInstallState is StoreQueueItemState.Error)
            {
                StatusMessage = $"Could not install the Microsoft Store update: {status.PackageInstallExtendedState}";
            }
        });
    }

    public void ReportStoreProgress(StorePackageUpdateStatus status) =>
        ReportStoreProgress(StoreUpdateProgress.From(
            status.PackageUpdateState switch
            {
                StorePackageUpdateState.Pending => AppUpdatePhase.Pending,
                StorePackageUpdateState.Downloading => AppUpdatePhase.Downloading,
                StorePackageUpdateState.Deploying or StorePackageUpdateState.Completed => AppUpdatePhase.Installing,
                StorePackageUpdateState.Canceled => AppUpdatePhase.None,
                _ => AppUpdatePhase.Failed,
            },
            status.PackageDownloadProgress,
            status.PackageBytesDownloaded,
            status.PackageDownloadSizeInBytes));

    private void ReportStoreProgress(StoreUpdateProgress progress)
    {
        var now = Environment.TickCount64;
        int generation;
        lock (_progressGate)
        {
            if (progress.Phase == _reportedPhase && now - _reportedAt < 250)
            {
                return;
            }

            _reportedPhase = progress.Phase;
            _reportedAt = now;
            generation = _progressGeneration;
        }

        RunOnUi(() =>
        {
            if (generation == Volatile.Read(ref _progressGeneration))
            {
                SetStoreProgress(progress);
            }
        });
    }

    private void SetStoreProgress(StoreUpdateProgress progress)
    {
        if (progress.Phase != AppUpdateProgress.Phase && progress.Phase is not AppUpdatePhase.None)
        {
            _logger.Info($"Store update: {progress.Phase.ToString().ToLowerInvariant()}");
        }

        AppUpdateProgress = progress;
    }

    private void FinishStoreProgress(string outcome, bool completed)
    {
        lock (_progressGate)
        {
            _progressGeneration++;
            _reportedPhase = AppUpdatePhase.None;
        }

        _logger.Info($"Store update: finished {outcome}");
        AppUpdateProgress = completed ? StoreUpdateProgress.From(AppUpdatePhase.Installing, 1, 0, 0) : StoreUpdateProgress.None;
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() => action());
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
            _ = ForceStoreScanAsync(scanAllApps: true);
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

    [RelayCommand(CanExecute = nameof(CanCheckOrInstallAppUpdate))]
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

    public event EventHandler? AppUpdateFocusRequested;

    private bool _pendingAppUpdateFocus;

    public bool TakeAppUpdateFocusRequest()
    {
        var pending = _pendingAppUpdateFocus;
        _pendingAppUpdateFocus = false;
        return pending;
    }

    private async Task HandleStoreUpdateBannerActionFromBannerAsync()
    {
        if (!IsSignedIn)
        {
            await HandleStoreUpdateBannerActionAsync().ConfigureAwait(true);
            return;
        }

        _pendingAppUpdateFocus = true;
        NavigateToPageTag?.Invoke("settings");
        AppUpdateFocusRequested?.Invoke(this, EventArgs.Empty);
        if (CheckOrInstallAppUpdateCommand.CanExecute(null))
        {
            await CheckOrInstallAppUpdateCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task StatusActionAsync() =>
        Failure == GateFailure.ClientOutdated ? HandleStoreUpdateBannerActionAsync()
        : HasApiRetry ? RetryApiAsync()
        : RefreshAsync();

    public void ReportClientOutdated(string message) => RunOnUi(() => EnterClientOutdated(message));

    private void EnterClientOutdated(string message)
    {
        if (Failure != GateFailure.ClientOutdated)
        {
            _logger.Warn(null, $"gigagrug answered client_outdated: {message}");
        }

        _clientOutdatedAt = DateTimeOffset.Now;
        Failure = GateFailure.ClientOutdated;
        StatusMessage = message;
        PropagateAuthorized();
    }

    private async Task CheckAndConfirmAppUpdateAsync()
    {
        IsCheckingAppUpdate = true;
        try
        {
            await CheckAppUpdateAsync(forceStoreScan: true, scanAllApps: true).ConfigureAwait(true);
        }
        finally
        {
            IsCheckingAppUpdate = false;
        }
    }

    private bool CanInstallAppUpdate => AppUpdate is not null && !IsAppUpdateInProgress;

    private bool CanCheckOrInstallAppUpdate => !IsAppUpdateInProgress;

    [RelayCommand(CanExecute = nameof(CanInstallAppUpdate))]
    private async Task InstallAppUpdateAsync()
    {
        if (AppUpdate is null || (IsAppUpdateInProgress && _storeInstall is not { IsCompleted: false }))
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
            SetStoreProgress(StoreUpdateProgress.From(AppUpdatePhase.Pending, 0, 0, 0));
            var operation = context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            operation.Progress = (_, status) => ReportStoreProgress(status);
            var result = await operation;
            FinishStoreProgress(result.OverallState.ToString(), result.OverallState is StorePackageUpdateState.Completed);
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
            FinishStoreProgress("with an error", completed: false);
            StatusMessage = $"Could not install the Microsoft Store update: {ex.Message}";
            OpenUri(_appUpdater.StoreUpdatesUri);
        }
    }

    private async Task CheckAsync(bool background, CancellationToken cancellationToken, bool pullGuildRoster = true)
    {
        var succeeded = true;
        var state = _stateStore.Load();
        foreach (var addon in VisibleAddons())
        {
            try
            {
                await ProbeAddonAsync(addon, state, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException)
            {
                _logger.Warn(ex, $"Addon manifest check failed for {addon.Id}");
                ReportFailure(ex, $"manifest:{addon.Id}", () => RetryProbeAddonAsync(addon.Id));
                succeeded = false;
            }
        }

        _lastCheckFailed = !succeeded;
        if (!await CheckProviderAddonsAsync(background).ConfigureAwait(true))
        {
            return;
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
            ReportFailure(ex, "auto-apply", async () =>
            {
                await AutoApplyAsync().ConfigureAwait(true);
                return true;
            });
        }

        if (pullGuildRoster)
        {
            await SyncRosterAsync().ConfigureAwait(true);
        }

        await NotifySavedVariablesChangedAsync().ConfigureAwait(true);
    }

    private async Task ProbeAddonAsync(ManagedAddon addon, AppState state, CancellationToken cancellationToken)
    {
        var releases = await _addonUpdater.ProbeChannelsAsync(addon, VisibleChannels, cancellationToken)
            .ConfigureAwait(true);
        _releases[addon.Id] = releases;
        _status[addon.Id] = AddonChannelStatus.Resolve(AddonGroups.StoredChannel(state.Channels, addon), releases, addon.Channels, addon.DefaultPreference);
        ResolveApiRetry($"manifest:{addon.Id}");
    }

    private async Task<bool> RetryProbeAddonAsync(string addonId)
    {
        if (VisibleAddons().FirstOrDefault(addon => addon.Id == addonId) is { } addon)
        {
            await ProbeAddonAsync(addon, _stateStore.Load(), CancellationToken.None).ConfigureAwait(true);
            ApplyStatus(background: true);
            RecomputeSummary();
        }

        return true;
    }

    private void SetGuilds(IReadOnlyList<AdminGuild> guilds, AdminGuild? current)
    {
        _isLoadingState = true;
        var wanted = guilds.Select(guild => (guild.Id, Role: GuildRoleLabel(guild))).ToList();
        if (!Guilds.Select(option => (option.Id, option.Role)).SequenceEqual(wanted))
        {
            Guilds.Clear();
            foreach (var guild in guilds)
            {
                Guilds.Add(new GuildOptionViewModel(guild, GuildRoleLabel(guild)));
            }
        }

        SelectedGuild = Guilds.FirstOrDefault(option => option.Id == current?.Id);
        foreach (var option in Guilds)
        {
            option.IsCurrent = option == SelectedGuild;
        }

        _isLoadingState = false;
        OnPropertyChanged(nameof(GuildPickerVisibility));
        OnPropertyChanged(nameof(HasGuildChoice));
        OnPropertyChanged(nameof(GuildChevronVisibility));
    }

    private string GuildRoleLabel(AdminGuild guild) =>
        GigagrugClient.ResolveGuildFeatures(guild, _features).Contains(GigagrugClient.StewardFeature) ? "Officer" : "Member";

    public void ChooseGuildOption(GuildOptionViewModel option)
    {
        ArgumentNullException.ThrowIfNull(option);

        if (option != SelectedGuild)
        {
            SelectedGuild = option;
            return;
        }

        _stateStore.Save(_stateStore.Load() with { GuildId = option.Id });
        _ = NotifySelectedGuildAsync(option.Id);
    }

    private async Task PromptForGuildAsync(string? storedGuildId)
    {
        if (_isChoosingGuild || ChooseGuild is null || !HasGuildChoice || Guilds.Any(option => option.Id == storedGuildId))
        {
            return;
        }

        _isChoosingGuild = true;
        try
        {
            var chosen = await ChooseGuild([.. Guilds], Guilds[0]).ConfigureAwait(true);
            if (chosen is null)
            {
                _guildPromptDeferred = true;
            }
            else if (Guilds.FirstOrDefault(option => option.Id == chosen.Id) is { } current)
            {
                _logger.Info($"Guild chosen at first run: {current.Id}");
                ChooseGuildOption(current);
            }
        }
        finally
        {
            _isChoosingGuild = false;
        }
    }

    private async Task RunGuildPromptAsync()
    {
        try
        {
            await PromptForGuildAsync(_stateStore.Load().GuildId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Guild prompt failed");
        }
    }

    public void ResumeGuildPrompt()
    {
        if (_guildPromptDeferred)
        {
            _guildPromptDeferred = false;
            _ = RunGuildPromptAsync();
        }
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

    public IReadOnlyList<CharacterPushRoute> CharacterRoutes(SavedVariablesSnapshot snapshot) =>
        CharacterPushRouting.Route(snapshot, CharacterPushTargets(), _guildId);

    private string CharacterPushTargetsKey() => string.Join(
        '|',
        CharacterPushTargets().Select(target =>
            $"{target.GuildId}/{target.ProfessionsOnly}/{(target.SyncGuildNames is null ? "-" : string.Join(',', target.SyncGuildNames))}"));

    private IReadOnlyList<CharacterPushTarget> CharacterPushTargets() =>
    [
        .. _meGuilds
            .Select(guild => (guild.Id, guild.SyncGuildNames, Features: GigagrugClient.ResolveGuildFeatures(guild, _features)))
            .Where(guild => guild.Features.Contains(GigagrugClient.SyncFeature))
            .Select(guild => new CharacterPushTarget(guild.Id, !guild.Features.Contains(GigagrugClient.StewardFeature), guild.SyncGuildNames)),
    ];

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
        _lastOfficerPayloadGuild = guildId;
        foreach (var install in Installs)
        {
            GuildRosterSync.WriteIfChanged(install.Install, payload, _stateStore, force, _logger);
        }

        return null;
    }

    public Task<string?> RestoreRosterAfterInstallAsync(WowInstall install)
    {
        if (_lastOfficerPayload is not { } payload || _lastOfficerPayloadGuild != _guildId)
        {
            return SyncRosterAsync();
        }

        GuildRosterSync.WriteIfChanged(install, payload with { Directory = _lastDirectory }, _stateStore, force: false, _logger);
        return Task.FromResult<string?>(null);
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
    public void ShowLocalInfoBanner(string message, string? id = null, string? actionLabel = null, Action? action = null, Action? dismissed = null, string? detail = null)
    {
        BannerViewModel? banner = null;
        var dismiss = new RelayCommand(() =>
        {
            _localBanners.Remove(banner!);
            dismissed?.Invoke();
            RefreshBanners();
        });
        banner = new BannerViewModel
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Revision = 0,
            Title = message,
            Message = detail ?? string.Empty,
            Background = (Brush)Application.Current.Resources["InfoTintBrush"],
            IconForeground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            IconGlyph = "",
            IsDismissible = true,
            Actions = action is null ? [] : [new BannerActionViewModel { Label = actionLabel!, Command = new RelayCommand(action) }],
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
                    actions.Add(new BannerActionViewModel { Label = action.Label, Command = new AsyncRelayCommand(HandleStoreUpdateBannerActionFromBannerAsync) });
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
            catch (ClientOutdatedException)
            {
                _logger.Info($"Guild event stream giving up for {guildId}: client_outdated");
                _isEventStreamLive = false;
                _eventsUnsupported = true;
                RecomputeLiveUpdatesState();
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
            catch (ClientOutdatedException)
            {
                _logger.Info("Access event stream giving up: client_outdated");
                _accessEventsUnsupported = true;
                _isAccessEventStreamLive = false;
                RecomputeLiveUpdatesState();
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

    public bool IsCharacterPushCurrent(string guildId, string flavourPath, string? charactersFingerprint)
    {
        if (charactersFingerprint is null)
        {
            return false;
        }

        var state = _stateStore.Load();
        var key = AppStateStore.CharacterSyncKey(guildId, flavourPath);
        return !CharacterPushGate.ShouldPush(charactersFingerprint, state.CharacterSync, key)
            && state.CharacterSync.GetValueOrDefault(key)?.Error is null;
    }

    public bool IsGuildDataWritten(string flavourPath, string addOnsPath) =>
        _stateStore.Load().GuildRosterSync.TryGetValue(flavourPath, out var written)
        && written == StewardSyncFile.ReadFingerprint(addOnsPath);

    public IReadOnlyDictionary<string, CharacterPushOutcome> GetCharacterOutcomes(string guildId, string flavourPath) =>
        _stateStore.Load().CharacterSync.GetValueOrDefault(AppStateStore.CharacterSyncKey(guildId, flavourPath))?.Characters
        ?? new Dictionary<string, CharacterPushOutcome>();

    public string DescribeRejection(string guildId, string characterGuid, string reason) =>
        reason == CharacterSyncRejectionCopy.NotLinkedReason && guildId == _guildId
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
        if (CharacterPushTargets().Count == 0)
        {
            return;
        }

        if (force && !await EnsureAuthorizedForActionAsync(CancellationToken.None).ConfigureAwait(true))
        {
            return;
        }

        if (CharacterPushTargets().Count == 0)
        {
            return;
        }

        foreach (var install in PresentInstalls.ToList())
        {
            if (await PushCharacterSyncAsync(install, force).ConfigureAwait(true))
            {
                return;
            }
        }
    }

    private async Task<bool> PushCharacterSyncAsync(WowInstallViewModel install, bool force)
    {
        SavedVariablesSnapshot? snapshot;
        try
        {
            snapshot = await Task.Run(() => StewardSavedVariables.Read(install.FlavourPath)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetCharacterSyncRow(install, $"Could not read the Steward saved variables: {ex.Message}", CharacterPushTargets().Select(target => target.GuildId));
            return false;
        }

        var routes = snapshot is null ? [] : CharacterRoutes(snapshot);
        if (routes.Count == 0)
        {
            if (force)
            {
                SetCharacterSyncRow(
                    install,
                    snapshot is { HasAccountData: true, Characters.Count: > 0 }
                        ? "No characters on a server's guild list."
                        : "No Steward saved variables with characters found for this install.",
                    []);
            }

            return false;
        }

        var attempted = false;
        var posted = false;
        string? error = null;
        foreach (var route in routes)
        {
            var result = await PushCharacterRouteAsync(install, snapshot!, route, force).ConfigureAwait(true);
            if (result.SignedOut)
            {
                return true;
            }

            attempted |= result.Attempted;
            posted |= result.Posted;
            error ??= result.Error is null || routes.Count == 1 ? result.Error : $"{GuildName(route.GuildId)}: {result.Error}";
        }

        if (attempted)
        {
            SetCharacterSyncRow(install, error, routes.Select(route => route.GuildId));
        }

        if (posted)
        {
            _lastDirectorySync = DateTimeOffset.Now;
            await SyncDirectoryAsync().ConfigureAwait(true);
        }

        return false;
    }

    private string GuildName(string guildId) => _meGuilds.FirstOrDefault(g => g.Id == guildId)?.Name ?? guildId;

    private async Task<(bool Attempted, bool Posted, bool SignedOut, string? Error)> PushCharacterRouteAsync(
        WowInstallViewModel install,
        SavedVariablesSnapshot snapshot,
        CharacterPushRoute route,
        bool force)
    {
        var guildId = route.GuildId;
        var key = AppStateStore.CharacterSyncKey(guildId, install.FlavourPath);
        var characters = route.Scope.Characters;
        var fingerprint = route.Scope.Fingerprint!;
        var state = _stateStore.Load();
        var last = state.CharacterSync.GetValueOrDefault(key);
        var plan = route.ProfessionsOnly
            ? ProfessionsPushSelection.Select(
                characters,
                snapshot.Professions,
                snapshot.Catalogue,
                last,
                guildId == _guildId ? _lastDirectory?.Characters : null,
                _userId,
                force,
                snapshot.Gear)
            : null;
        var gateOpen = plan is null
            ? CharacterPushGate.ShouldPush(fingerprint, state.CharacterSync, key)
            : plan.HasWork && !(last is { Error: not null } && last.Fingerprint == fingerprint);
        if ((!force && !gateOpen) || !CharacterPushTargets().Any(target => target.GuildId == guildId))
        {
            return (false, false, false, null);
        }

        if (force)
        {
            state.CharacterSyncBatches.Remove(key);
        }

        var sentCharacters = plan?.Characters ?? characters;
        var sentCatalogue = plan is { SendCatalogue: false } || snapshot.Catalogue.Count == 0 ? null : snapshot.Catalogue;
        var batchFingerprint = plan is null ? fingerprint : CharacterSyncMapping.Fingerprint(sentCharacters, snapshot.Professions, sentCatalogue, gear: snapshot.Gear);
        var batchId = ResolveBatchId(state, key, batchFingerprint);
        var request = new CharacterSyncRequest(
            batchId,
            InstalledVersion,
            [.. sentCharacters.Select(c => CharacterSyncMapping.ToEntry(c, snapshot.Professions, snapshot.Gear))],
            sentCatalogue,
            route.Scope.GuildRanks is null ? null : CharacterSyncMapping.ToSync(route.Scope.GuildRanks));

        try
        {
            var result = await _gigagrugClient.PostCharacterSyncAsync(guildId, request, CancellationToken.None).ConfigureAwait(true);
            // gigagrug answers a replayed batchId with accepted 0 and no rejections, which says nothing about each character.
            if (plan is not null && result.Replay)
            {
                state = _stateStore.Load();
                state.CharacterSyncBatches.Remove(key);
                _stateStore.Save(state);
                return (false, false, false, null);
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
            state.CharacterSync[key] = new CharacterPushRecord(fingerprint, DateTimeOffset.Now, result.Accepted, Characters: outcomes, CatalogueFingerprint: catalogueFingerprint);
            _stateStore.Save(state);
            _logger.Info(
                $"Character push for {guildId} {install.FlavourPath}: {result.Accepted} accepted, {result.Rejected.Count} rejected");
            return (true, true, false, null);
        }
        catch (SessionExpiredException)
        {
            _logger.Info($"Character push for {guildId} {install.FlavourPath}: 401, session expired");
            SignOutTo(GateFailure.SessionExpired);
            return (true, false, true, null);
        }
        catch (GigagrugRequestException ex)
        {
            _logger.Warn(ex, $"Character push for {guildId} {install.FlavourPath} rejected, status={ex.StatusCode}");
            var message = ex.StatusCode == HttpStatusCode.Forbidden
                ? "Your account can't sync this guild."
                : ex.Body ?? ex.Message;
            state = _stateStore.Load();
            state.CharacterSync[key] = plan is null
                ? new CharacterPushRecord(fingerprint, DateTimeOffset.Now, 0, message)
                : new CharacterPushRecord(fingerprint, DateTimeOffset.Now, 0, message, last?.Characters, last?.CatalogueFingerprint);
            _stateStore.Save(state);
            return (true, false, false, message);
        }
        catch (GigagrugThrottledException)
        {
            _logger.Info($"Character push for {guildId} {install.FlavourPath}: 429, throttled");
            return (true, false, false, "Sent too recently, trying again shortly");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.Warn(ex, $"Character push for {guildId} {install.FlavourPath} failed, gigagrug unreachable");
            return (true, false, false, "gigagrug unreachable, retrying");
        }
    }

    private string ResolveBatchId(AppState state, string key, string fingerprint)
    {
        var pending = state.CharacterSyncBatches.GetValueOrDefault(key);
        var batchId = CharacterPushGate.ResolveBatchId(pending, fingerprint, Guid.NewGuid().ToString());
        state.CharacterSyncBatches[key] = new CharacterSyncBatch(fingerprint, batchId);
        _stateStore.Save(state);
        return batchId;
    }

    private IReadOnlyList<string> RoutedGuildIds(string flavourPath)
    {
        try
        {
            return StewardSavedVariables.Read(flavourPath) is { } snapshot ? [.. CharacterRoutes(snapshot).Select(route => route.GuildId)] : [];
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static DateTimeOffset? LastCharacterPushAt(AppState state, string flavourPath, IEnumerable<string> guildIds) =>
        guildIds
            .Select(guildId => state.CharacterSync.GetValueOrDefault(AppStateStore.CharacterSyncKey(guildId, flavourPath))?.PushedAt)
            .Max();

    private void SetCharacterSyncRow(WowInstallViewModel install, string? error, IEnumerable<string> routedGuildIds)
    {
        var pushedAt = LastCharacterPushAt(_stateStore.Load(), install.FlavourPath, routedGuildIds);
        var existing = CharacterSyncRows.FirstOrDefault(row => row.FlavourPath == install.FlavourPath);
        var row = new CharacterSyncRowViewModel(install.DisplayName, install.FlavourPath, pushedAt, error);
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

        RestedXp.SetInstalls(PresentInstalls);
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
            await RunBoundedAsync(Installs.ToList().SelectMany(install => install.TopRows.ToList().Select(row => (Func<Task>)(() =>
                (mode != AutoUpdateMode.OutOfGame || !install.IsClientRunning)
                && row.CanAutoApply
                && !AppStateStore.IsExcludedFromUpdates(state, install.FlavourPath, row.AddonId)
                    ? row.UpdateCommand.ExecuteAsync(null)
                    : Task.CompletedTask)))).ConfigureAwait(true);
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
        var visibleIds = AllVisibleAddons().Select(addon => addon.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            install.SyncAddons(AddonsFor(install.FlavourPath));
        }

        RebuildAddonChannels();
        SyncRestedXpRows();
        SyncCharacterSyncRows();
        OnPropertyChanged(nameof(IsCurseForgeEnabled));
        OnPropertyChanged(nameof(CurseForgeSettingVisibility));
        OnPropertyChanged(nameof(GetAddonsVisibility));
    }

    private void SyncCharacterSyncRows()
    {
        if (!CanPushCharacters)
        {
            CharacterSyncRows.Clear();
            return;
        }

        var known =new HashSet<string>(Installs.Select(install => install.FlavourPath), StringComparer.OrdinalIgnoreCase);
        foreach (var gone in CharacterSyncRows.Where(row => !known.Contains(row.FlavourPath)).ToList())
        {
            CharacterSyncRows.Remove(gone);
        }

        var state = _stateStore.Load();
        foreach (var install in Installs)
        {
            var existing = CharacterSyncRows.FirstOrDefault(r => r.FlavourPath == install.FlavourPath);
            if (existing is not null && _characterRowsGuildId == _guildId)
            {
                continue;
            }

            var routed = RoutedGuildIds(install.FlavourPath);
            var error = routed
                .Select(guildId => state.CharacterSync.GetValueOrDefault(AppStateStore.CharacterSyncKey(guildId, install.FlavourPath))?.Error)
                .FirstOrDefault(message => message is not null);
            var row = new CharacterSyncRowViewModel(install.DisplayName, install.FlavourPath, LastCharacterPushAt(state, install.FlavourPath, routed), error);
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
        var visible = AllVisibleAddons();
        var visibleIds = visible.Select(addon => addon.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in AddonChannels.Where(channel => !visibleIds.Contains(channel.AddonId)).ToList())
        {
            AddonChannels.Remove(gone);
        }

        foreach (var addon in visible)
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

    partial void OnCurseForgeEnabledChanged(bool value)
    {
        if (_isLoadingState)
        {
            return;
        }

        _stateStore.Save(_stateStore.Load() with { CurseForgeEnabled = value });
        ReconcileFeatureGating();
        foreach (var install in Installs)
        {
            _ = install.RescanLocalAsync();
        }

        OnPropertyChanged(nameof(UpdateAllVisibility));
        OnPropertyChanged(nameof(AdoptAllVisibility));
        OnPropertyChanged(nameof(AdoptAllAccessibleName));
        _ = EvaluateCurseForgeDefaultHandlerAsync();
        if (value && IsCurseForgeEnabled)
        {
            _ = ProbeAddedProviderAddonsAsync(AllProviderAddons());
        }
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

        if (AllVisibleAddons().FirstOrDefault(a => string.Equals(a.Id, addonId, StringComparison.OrdinalIgnoreCase)) is not { } addon)
        {
            return;
        }

        _status[addonId] = AddonChannelStatus.Resolve(channel, releases, addon.Channels, addon.DefaultPreference);
        foreach (var child in AllVisibleAddons().Where(child => string.Equals(child.Parent, addonId, StringComparison.OrdinalIgnoreCase)))
        {
            if (_releases.TryGetValue(child.Id, out var childReleases))
            {
                _status[child.Id] = AddonChannelStatus.Resolve(channel, childReleases, child.Channels, child.DefaultPreference);
            }
        }

        ApplyStatus(background: false);
        RecomputeSummary();
    }

    private WowInstallViewModel AddInstall(WowInstall install, bool isAddedByUser)
    {
        var viewModel = new WowInstallViewModel(
            install,
            install.ProductCode is { } product && _supportedProducts.TryGetValue(product, out var productName) ? productName : null,
            _stateStore.Load().InstallLabels.GetValueOrDefault(install.FlavourPath),
            AddonsFor(install.FlavourPath),
            ExcludedFolders,
            IdentifyCurseForgeAsync,
            ReconcileProviderAddons,
            UnmanageProviderAddon,
            ConfirmAdoptAsync,
            _addonUpdater,
            _stateStore,
            (addon, ct) => EnsureAuthorizedForAddonAsync(install.FlavourPath, addon, ct),
            () => IsCurseForgeEnabled,
            ShowChannelDialogFor,
            ConfirmUninstallAsync,
            install => _ = ConfirmRemoveInstallAsync(install.FlavourPath),
            OnClientExited,
            wowInstall => AfterStewardInstalled?.Invoke(wowInstall) ?? Task.CompletedTask,
            _logger)
        {
            IsAddedByUser = isAddedByUser,
        };
        viewModel.SetIsAdmin(IsAdminFor);
        viewModel.RowsChanged += OnInstallRowsChanged;
        Installs.Add(viewModel);
        RebuildAddonChannels();
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
        RestedXp.RefreshRelativeTimes();
        _ = NotifySavedVariablesChangedAsync();
        if (!_isChecking)
        {
            _ = RunBackgroundPassAsync();
            return;
        }

        RefreshClients();
        _ = AutoApplyAsync();
    }

    private async Task<bool> RunBackgroundPassAsync()
    {
        if (_isChecking)
        {
            return false;
        }

        _isChecking = true;
        try
        {
            UpdateStoreAppInstalledState();

            var result = Failure == GateFailure.ClientOutdated && DateTimeOffset.Now - _clientOutdatedAt < GuildSyncFallbackInterval
                ? AuthCheckResult.ClientOutdated
                : _isAccessEventStreamLive && IsAuthorized && !IsDue(_lastAuthCheck, GuildSyncFallbackInterval)
                    ? AuthCheckResult.Authorized
                    : await RecheckAuthorizationAsync(CancellationToken.None).ConfigureAwait(true);
            if (result == AuthCheckResult.Authorized)
            {
                var guildSyncDue = !_isEventStreamLive || IsDue(_lastGuildSync, GuildSyncFallbackInterval);
                await CheckAsync(background: true, CancellationToken.None, guildSyncDue).ConfigureAwait(true);
                if (guildSyncDue)
                {
                    _lastGuildSync = DateTimeOffset.Now;
                    await PushCharacterSyncAsync().ConfigureAwait(true);
                }
                if (IsDue(_lastDirectorySync, GuildSyncFallbackInterval))
                {
                    _lastDirectorySync = DateTimeOffset.Now;
                    await SyncDirectoryAsync().ConfigureAwait(true);
                }
                if (IsDue(_lastBannersSync, GuildSyncFallbackInterval))
                {
                    _lastBannersSync = DateTimeOffset.Now;
                    await SyncBannersAsync().ConfigureAwait(true);
                }
                if (!App.IsPackaged || IsDue(_lastStoreCheck, StoreCheckInterval))
                {
                    await CheckAppUpdateAsync().ConfigureAwait(true);
                }
            }
            else if (result == AuthCheckResult.Unreachable && _addonCatalogue is not null)
            {
                await CheckAsync(background: true, CancellationToken.None, pullGuildRoster: false).ConfigureAwait(true);
            }

            await RestedXp.RefreshSessionAsync().ConfigureAwait(true);
            if (DateTimeOffset.Now - _lastGuideCheck >= _nextGuideCheckDue)
            {
                await CheckGuidesAsync().ConfigureAwait(true);
            }

            ResolveApiRetry("background-pass");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ReportFailure(ex, "background-pass", RunBackgroundPassAsync);
            return false;
        }
        finally
        {
            _isChecking = false;
        }
    }

    // A Store update restarts every copy at once, so each copy stretches its intervals by its own 0 to 20% to fall out of step.
    private bool IsDue(DateTimeOffset last, TimeSpan interval) => DateTimeOffset.Now - last >= interval * _intervalJitter;

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
        await RecheckForActionAsync(cancellationToken).ConfigureAwait(true) == AuthCheckResult.Authorized;

    private async Task<bool> EnsureAuthorizedForAddonAsync(string flavourPath, ManagedAddon addon, CancellationToken cancellationToken)
    {
        var result = await RecheckForActionAsync(cancellationToken).ConfigureAwait(true);
        return Failure != GateFailure.ClientOutdated
            && AddonsFor(flavourPath).Any(visible => string.Equals(visible.Id, addon.Id, StringComparison.OrdinalIgnoreCase))
            && (result == AuthCheckResult.Authorized
                || (result == AuthCheckResult.Unreachable && AddonCatalogue.UpdatesWhileUnreachable(_addonCatalogue, addon.Id)));
    }

    private Task<AuthCheckResult> RecheckForActionAsync(CancellationToken cancellationToken) =>
        _actionAuthorization is { IsCompleted: false } inFlight
            ? inFlight
            : _actionAuthorization = RecheckAuthorizationAsync(cancellationToken);

    private async Task<AuthCheckResult> RecheckAuthorizationAsync(CancellationToken cancellationToken)
    {
        _lastAuthCheck = DateTimeOffset.Now;
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

            var storedGuildId = _stateStore.Load().GuildId;
            var guild = me.ResolveGuild(storedGuildId);
            _guildId = guild?.Id;
            var previousPushTargets = CharacterPushTargetsKey();
            _meGuilds = me.Guilds;

            var previousFeatures = new HashSet<string>(_features, StringComparer.Ordinal);
            var wasCurseForgeEnabled = IsCurseForgeEnabled;
            var previousRole = Role;
            var previousUserId = _userId;
            _features.Clear();
            _features.UnionWith(features);
            UpdateGuildFeatures();
            var catalogueChanged = SetAddonCatalogue(me.Addons);
            if (catalogueChanged || !previousFeatures.SetEquals(_features))
            {
                ReconcileFeatureGating();
            }

            if (catalogueChanged || wasCurseForgeEnabled != IsCurseForgeEnabled)
            {
                foreach (var install in Installs)
                {
                    _ = install.RescanLocalAsync();
                }
            }

            UserName = me.User.Name;
            UserHandle = me.User.Username is { Length: > 0 } u ? $"@{u}" : null;
            Role = me.User.Role;
            _userId = me.User.Id;
            AvatarUri = Uri.TryCreate(me.User.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null;
            IsAuthorized = true;
            _ = EvaluateCurseForgeDefaultHandlerAsync();
            SetGuilds(me.Guilds, guild);
            _ = RunGuildPromptAsync();
            if (Failure != GateFailure.ClientOutdated)
            {
                StatusMessage = null;
                Failure = GateFailure.None;
            }

            ResolveApiRetry("me");

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
            if (previousPushTargets != CharacterPushTargetsKey())
            {
                _characterRowsGuildId = null;
                SyncCharacterSyncRows();
                OnPropertyChanged(nameof(CanPushCharacters));
                OnPropertyChanged(nameof(SyncVisibility));
                _ = SavedVariablesWrittenAsync();
            }

            _logger.Info($"/api/me recheck: authorized, role={Role}, {_features.Count} feature(s)");
            return AuthCheckResult.Authorized;
        }
        catch (SessionExpiredException)
        {
            _logger.Info("/api/me recheck: 401, session expired");
            SignOutTo(GateFailure.SessionExpired);
            return AuthCheckResult.SessionExpired;
        }
        catch (ClientOutdatedException ex)
        {
            EnterClientOutdated(ex.Message);
            return AuthCheckResult.ClientOutdated;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.Warn(ex, "/api/me recheck: gigagrug unreachable");
            if (Failure != GateFailure.ClientOutdated)
            {
                Failure = GateFailure.Unreachable;
                ReportFailure(ex, "me", async () => await RecheckAuthorizationAsync(CancellationToken.None).ConfigureAwait(true) == AuthCheckResult.Authorized);
            }

            PropagateAuthorized();
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
        SetAddonCatalogue(null);
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
        _apiRetries.Clear();
        FinishApiRetries();
        StatusMessage = null;
        IsSignedIn = false;
        Failure = failure;
        _ = EvaluateCurseForgeDefaultHandlerAsync();
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
        _lastOfficerPayloadGuild = null;
        OnPropertyChanged(nameof(IsCurseForgeEnabled));
        OnPropertyChanged(nameof(CurseForgeSettingVisibility));
        OnPropertyChanged(nameof(GetAddonsVisibility));
    }

    private void PropagateAuthorized()
    {
        foreach (var install in Installs)
        {
            install.SetIsAdmin(IsAdminFor);
        }

        ApplyChannelStatus();
    }

    private enum AuthCheckResult
    {
        Authorized,
        NotAuthorized,
        SessionExpired,
        Unreachable,
        ClientOutdated,
    }
}
