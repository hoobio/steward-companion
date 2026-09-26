using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.ApplicationModel;
using Windows.Management.Deployment;
using Windows.Services.Store;
using Windows.Storage.Pickers;

namespace Steward.App.ViewModels;

public enum GateFailure
{
    None,
    Timeout,
    SessionExpired,
    Unreachable,
    NotAuthorized,
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan GuideCheckInterval = TimeSpan.FromHours(3);

    private static readonly TimeSpan StoreCheckInterval = TimeSpan.FromHours(1);

    private const string StartupTaskId = "StewardStartup";

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
        nameof(NoInstallsVisibility),
        nameof(IsAnyRowBusy),
        nameof(InstallsDescription),
        nameof(HiddenToggleVisibility),
        nameof(GuidesVisibility),
        nameof(SyncVisibility),
    ];

    private readonly ISessionService _sessionService;
    private readonly GigagrugClient _gigagrugClient;
    private readonly GigagrugGuildSyncApi _guildSyncApi;
    private readonly AddonUpdater _addonUpdater;
    private readonly AppUpdater _appUpdater;
    private readonly AppStateStore _stateStore;
    private readonly IReadOnlyList<ManagedAddon> _addons;
    private readonly IReadOnlyDictionary<string, string> _supportedProducts;
    private readonly Dictionary<string, AddonChannelStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, AddonRelease?>> _releases =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _features = new(StringComparer.Ordinal);

    private DispatcherQueueTimer? _recheckTimer;
    private CancellationTokenSource? _signInCts;
    private string? _guildId;
    private DateTimeOffset _lastPass;
    private DateTimeOffset _lastGuideCheck;
    private DateTimeOffset _lastStoreCheck;
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
        IGuildSyncApi syncApi,
        RestedXpService restedXpService)
    {
        ArgumentNullException.ThrowIfNull(addons);
        ArgumentNullException.ThrowIfNull(supportedProducts);

        _sessionService = sessionService;
        _gigagrugClient = gigagrugClient;
        _guildSyncApi = guildSyncApi;
        _addonUpdater = addonUpdater;
        _appUpdater = appUpdater;
        _stateStore = stateStore;
        _addons = addons;
        _supportedProducts = supportedProducts;

        var state = stateStore.Load();
        _isLoadingState = true;
        MinimizeToTray = state.MinimizeToTray;
        CloseToTray = state.CloseToTray;
        AppChannelIndex = state.AppChannel == "pre-release" ? 1 : 0;
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

        Sync = new SyncViewModel(this, syncApi)
        {
            StateChanged = () => OnPropertyChanged(nameof(SyncBadgeVisibility)),
        };
        SavedVariablesChanged = Sync.ReloadAsync;
        AfterStewardInstalled = _ => Sync.WriteGeneratedFileAsync();
    }

    public SyncViewModel Sync { get; }

    public RestedXpViewModel RestedXp { get; }

    public ObservableCollection<WowInstallViewModel> Installs { get; } = [];

    public ObservableCollection<AddonChannelViewModel> AddonChannels { get; } = [];

    public ObservableCollection<GuildOptionViewModel> Guilds { get; } = [];

    public nint OwnerWindowHandle { get; set; }

    public Action? NavigateToSettings { get; set; }

    public Action? NavigateToAddons { get; set; }

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
    [NotifyPropertyChangedFor(nameof(HeaderSubtitle), nameof(BannerDetail))]
    public partial string LastCheckedRelative { get; set; } = "not checked yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GateVisibility), nameof(ShellChromeVisibility))]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GateHeading), nameof(CancelSignInVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial bool IsSigningIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeoutVisibility), nameof(SessionExpiredVisibility), nameof(UnreachableVisibility), nameof(NotAuthorizedVisibility), nameof(IsApiReachable), nameof(RetryVisibility))]
    public partial GateFailure Failure { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool CanStartWithWindows { get; set; } = App.IsPackaged || App.IsGitHubRelease;

    [ObservableProperty]
    public partial string StartWithWindowsDescription { get; set; } = App.IsPackaged || App.IsGitHubRelease
        ? "Starts Steward in the tray when you sign in to Windows"
        : "Only available in a released build";

    [ObservableProperty]
    public partial int AppChannelIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoreListingVisibility), nameof(StoreSwitchVisibility))]
    public partial bool? IsStoreAppInstalled { get; set; }

    [ObservableProperty]
    public partial bool ShowHiddenAddons { get; set; }

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
    [NotifyPropertyChangedFor(nameof(AppUpdateVisibility), nameof(AppUpdateTitle), nameof(AppUpdateChangelogUri), nameof(AboutDescription), nameof(AboutActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(InstallAppUpdateCommand))]
    public partial AddonRelease? AppUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppUpdateActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(InstallAppUpdateCommand))]
    public partial bool IsInstallingAppUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppUpdateActionLabel))]
    public partial double AppUpdateProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AboutDescription), nameof(AboutActionLabel))]
    public partial bool IsCheckingAppUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AboutDescription))]
    public partial bool IsLatestConfirmed { get; set; }

    public Visibility AppUpdateVisibility => When(AppUpdate is not null);

    public string AppUpdateTitle => App.IsPackaged
        ? "A Steward update is available in the Microsoft Store"
        : $"Steward {AppUpdate?.Version.TrimStart('v')} is available";

    public string AppUpdateActionLabel => App.IsPackaged ? "Open the Store"
        : IsInstallingAppUpdate ? $"Downloading {AppUpdateProgress:P0}" : "Install and restart";

    public Uri? AppUpdateChangelogUri => AppUpdate is null ? null
        : new Uri(App.IsPackaged ? "https://github.com/hoobio/steward-companion/releases/latest" : $"https://github.com/hoobio/steward-companion/releases/tag/{AppUpdate.Version}");

    public Visibility StoreListingVisibility => When(IsStoreAppInstalled == false);

    public Visibility StoreSwitchVisibility => When(IsStoreAppInstalled == true);

    public string AboutActionLabel => IsCheckingAppUpdate ? "Checking" : AppUpdate is null ? "Check for a new version" : "Install update";

    private string AppChannel => AppChannelIndex == 1 ? "pre-release" : "release";

    private IReadOnlyList<string> VisibleChannels => IsGlobalAdmin ? AddonChannelStatus.Ordered : ["release", "pre-release"];

    public bool HasGuidesFeature => _features.Contains(GigagrugClient.GuidesFeature);

    private bool HasStewardFeature => _features.Contains(GigagrugClient.StewardFeature);

    private IReadOnlyList<ManagedAddon> VisibleAddons() =>
        [.. _addons.Where(addon => addon.Features.Any(_features.Contains))];

    public int InstallCount => Installs.Count;

    public int AddonCount => Installs.Sum(install => install.AddonRows.Count(row => !row.IsHidden));

    public int UpdateCount => Installs.Sum(install => install.UpdateCount);

    public bool IsAnyRowBusy => Installs.Any(install => install.AddonRows.Any(row => row.IsBusy));

    public string HeaderSubtitle =>
        _lastPass == default ? "Not checked for updates yet" : $"Last checked for updates {LastCheckedRelative}";

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
                return $"Last checked {LastCheckedRelative}";
            }

            var first = Installs
                .SelectMany(install => install.AddonRows)
                .FirstOrDefault(row => !row.IsHidden && row.HasUpdateAvailable);
            if (first is null)
            {
                return string.Empty;
            }

            var line = $"{first.DisplayName} {first.InstalledVersion ?? "not installed"} → {first.AvailableVersion}";
            return UpdateCount > 1 ? $"{line}, and {UpdateCount - 1} more" : line;
        }
    }

    public Visibility BannerVisibility => When(Installs.Count > 0);

    public Visibility UpdateAllVisibility => When(IsAuthorized && UpdateCount > 0);

    public Visibility NoInstallsVisibility => When(Installs.Count == 0 && !IsBusy);

    public Visibility HiddenToggleVisibility => When(Installs.Any(install => install.AddonRows.Any(row => row.IsHidden)));

    public bool StatusMessageIsOpen => !string.IsNullOrEmpty(StatusMessage);

    public Visibility GateVisibility => When(!IsSignedIn);

    public Visibility ShellChromeVisibility => When(IsSignedIn);

    public Visibility SyncBadgeVisibility => When(Sync.HasWaiting);

    public Visibility GuidesVisibility => When(IsGuidesPreview
        || (HasGuidesFeature && RestedXp.IsSignedIn && Installs.Any(install => install.AddonRows.Any(row =>
            string.Equals(row.AddonId, RestedXpViewModel.AddonId, StringComparison.OrdinalIgnoreCase)
            && row.State is not (AddonRowState.Missing or AddonRowState.NoReleases)))));

    public Visibility SyncVisibility => When(HasStewardFeature);

    public Visibility TimeoutVisibility => When(Failure == GateFailure.Timeout);

    public Visibility SessionExpiredVisibility => When(Failure == GateFailure.SessionExpired);

    public Visibility UnreachableVisibility => When(Failure == GateFailure.Unreachable);

    public Visibility NotAuthorizedVisibility => When(Failure == GateFailure.NotAuthorized);

    public bool IsApiReachable => Failure != GateFailure.Unreachable;

    public Visibility RetryVisibility => When(Failure == GateFailure.Unreachable);

    public Visibility CancelSignInVisibility => When(IsSigningIn);

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

    public string VersionLabel { get; } =
        $"Steward {InstalledVersion}{(App.IsGitHubRelease ? "" : " (Development)")}";

    public static string WindowTitle => App.IsGitHubRelease ? "Steward" : "Steward (Development)";

    public string InstallsDescription => $"{InstallCount} found, read from .flavor.info and .build.info";

    public string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Steward");

    public string StatePath => Path.Combine(DataFolder, "state.json");

    public string AboutDescription =>
        $"{VersionLabel}, installed to {App.DisplayDataFolder(DataFolder)}{(AppUpdate is null ? "" : App.IsPackaged ? ", an update is available" : $", {AppUpdate.Version.TrimStart('v')} available")}{(IsLatestConfirmed && AppUpdate is null ? ", up to date" : "")}";

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public void Dispose()
    {
        _recheckTimer?.Stop();
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
        await RestedXp.CheckAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (App.IsPackaged)
        {
            await SetStartupTaskAsync(enable: null).ConfigureAwait(true);
        }

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
        ResetInstalls();
        _guildId = null;
        SetGuilds([], null);
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
            UpdateStoreAppInstalledState();

            if (await RecheckAuthorizationAsync(cancellationToken).ConfigureAwait(true)
                is AuthCheckResult.SessionExpired or AuthCheckResult.NotAuthorized)
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
            await CheckAppUpdateAsync(cancellationToken).ConfigureAwait(true);
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
        SyncGuideInstalls();
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
            UpdateStoreAppInstalledState();

            if (await RecheckAuthorizationAsync(CancellationToken.None).ConfigureAwait(true)
                is AuthCheckResult.SessionExpired or AuthCheckResult.NotAuthorized)
            {
                return;
            }

            GitHubReleases.ResetCache();
            await CheckAsync(background: false, CancellationToken.None).ConfigureAwait(true);
            await CheckAppUpdateAsync(CancellationToken.None).ConfigureAwait(true);
            await CheckGuidesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            RecomputeSummary();
        }
    }

    private async Task CheckAppUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            AppUpdate = App.IsPackaged
                ? App.IsGitHubRelease ? await CheckStoreUpdateAsync().ConfigureAwait(true) : null
                : await _appUpdater
                    .CheckAsync(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0), InstalledVersion, AppChannel, cancellationToken)
                    .ConfigureAwait(true);
            if (AppUpdate is not null)
            {
                IsLatestConfirmed = false;
            }
        }
        catch (GitHubRateLimitedException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or COMException)
        {
            StatusMessage = $"Could not check for a Steward update: {ex.Message}";
        }
    }

    private void UpdateStoreAppInstalledState()
    {
        if (!App.IsPackaged)
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
            return updates.Count == 0 ? null : new AddonRelease(string.Empty, _appUpdater.StoreListingUri.OriginalString, string.Empty, 0, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not check the Microsoft Store for an update: {ex.Message}";
            return null;
        }
    }

    [RelayCommand]
    private void OpenStoreListing() =>
        Process.Start(new ProcessStartInfo(_appUpdater.StoreListingUri.OriginalString) { UseShellExecute = true })?.Dispose();

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
            CanStartWithWindows = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
            StartWithWindowsDescription = state switch
            {
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
        if (AppUpdate is not null)
        {
            await InstallAppUpdateAsync().ConfigureAwait(true);
            return;
        }

        await CheckAndConfirmAppUpdateAsync().ConfigureAwait(true);
    }

    private async Task CheckAndConfirmAppUpdateAsync()
    {
        IsCheckingAppUpdate = true;
        try
        {
            GitHubReleases.ResetCache();
            await CheckAppUpdateAsync(CancellationToken.None).ConfigureAwait(true);
            IsLatestConfirmed = AppUpdate is null;
        }
        finally
        {
            IsCheckingAppUpdate = false;
        }
    }

    private bool CanInstallAppUpdate => AppUpdate is not null && !IsInstallingAppUpdate;

    [RelayCommand(CanExecute = nameof(CanInstallAppUpdate))]
    private async Task InstallAppUpdateAsync()
    {
        if (AppUpdate is not { } release)
        {
            return;
        }

        if (App.IsPackaged)
        {
            OpenStoreListing();
            return;
        }

        IsInstallingAppUpdate = true;
        AppUpdateProgress = 0;
        try
        {
            var msiPath = await _appUpdater.DownloadAsync(release, new Progress<double>(value => AppUpdateProgress = value), CancellationToken.None)
                .ConfigureAwait(true);
            AppUpdater.InstallAfterExit(msiPath, Path.Combine(DataFolder, "update.log"));
            QuitRequested?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            StatusMessage = $"Update failed: {ex.Message}";
            IsInstallingAppUpdate = false;
        }
    }

    private async Task CheckAsync(bool background, CancellationToken cancellationToken)
    {
        var succeeded = true;
        try
        {
            var state = _stateStore.Load();
            foreach (var addon in VisibleAddons())
            {
                var releases = await _addonUpdater.ProbeChannelsAsync(addon, addon.IsGitHub ? addon.Channels : VisibleChannels, cancellationToken)
                    .ConfigureAwait(true);
                _releases[addon.Id] = releases;
                _status[addon.Id] = AddonChannelStatus.Resolve(state.Channels.GetValueOrDefault(addon.Id), releases, addon.Channels, addon.DefaultPreference);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException or GitHubRateLimitedException)
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
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or OperationCanceledException or GitHubRateLimitedException)
        {
            StatusMessage = ex.Message;
        }

        await SyncRosterAsync().ConfigureAwait(true);
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
        foreach (var option in Guilds)
        {
            option.IsCurrent = option == value;
        }

        _ = SyncRosterAsync();
    }

    public async Task<string?> SyncRosterAsync()
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
            var message = $"Could not pull the guild roster: {ex.Message}";
            StatusMessage = message;
            return message;
        }

        foreach (var install in Installs)
        {
            GuildRosterSync.WriteIfChanged(install.Install, payload, _stateStore);
        }

        return null;
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

    partial void OnAppChannelIndexChanged(int value)
    {
        if (_isLoadingState)
        {
            return;
        }

        _stateStore.Save(_stateStore.Load() with { AppChannel = AppChannel });
        _ = CheckAndConfirmAppUpdateAsync();
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

    partial void OnShowHiddenAddonsChanged(bool value)
    {
        foreach (var install in Installs)
        {
            install.SetShowHidden(value);
        }
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
            VisibleAddons(),
            _addonUpdater,
            _stateStore,
            EnsureAuthorizedForActionAsync,
            _features.Contains,
            () => NavigateToSettings?.Invoke(),
            RemoveInstall,
            OnClientExited,
            wowInstall => AfterStewardInstalled?.Invoke(wowInstall) ?? Task.CompletedTask)
        {
            IsAddedByUser = isAddedByUser,
        };
        viewModel.SetIsAdmin(IsAuthorized);
        viewModel.SetShowHidden(ShowHiddenAddons);
        viewModel.RowsChanged += OnInstallRowsChanged;
        Installs.Add(viewModel);
        SyncGuideInstalls();
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
    }

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
                await CheckAsync(background: true, CancellationToken.None).ConfigureAwait(true);
                if (!App.IsPackaged || DateTimeOffset.Now - _lastStoreCheck >= StoreCheckInterval)
                {
                    await CheckAppUpdateAsync(CancellationToken.None).ConfigureAwait(true);
                }
            }

            await RestedXp.RefreshSessionAsync().ConfigureAwait(true);
            if (DateTimeOffset.Now - _lastGuideCheck >= GuideCheckInterval)
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
            if (features.Count == 0)
            {
                SignOutTo(GateFailure.NotAuthorized);
                return AuthCheckResult.NotAuthorized;
            }

            var previousFeatures = new HashSet<string>(_features, StringComparer.Ordinal);
            _features.Clear();
            _features.UnionWith(features);
            if (!previousFeatures.SetEquals(_features))
            {
                ReconcileFeatureGating();
            }

            UserName = me.User.Name;
            UserHandle = me.User.Username is { Length: > 0 } u ? $"@{u}" : null;
            Role = me.User.Role;
            AvatarUri = Uri.TryCreate(me.User.AvatarUrl, UriKind.Absolute, out var avatar) ? avatar : null;
            IsAuthorized = true;
            var guild = me.ResolveGuild(_stateStore.Load().GuildId);
            _guildId = guild?.Id;
            SetGuilds(me.Guilds, guild);
            StatusMessage = null;
            Failure = GateFailure.None;

            // Client-side gate only: gigagrug does not restrict who can fetch the unstable manifest.
            IsGlobalAdmin = GigagrugClient.IsGlobalAdmin(me);

            PropagateAuthorized();
            return AuthCheckResult.Authorized;
        }
        catch (SessionExpiredException)
        {
            SignOutTo(GateFailure.SessionExpired);
            return AuthCheckResult.SessionExpired;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Failure = GateFailure.Unreachable;
            StatusMessage = ex.Message;
            return AuthCheckResult.Unreachable;
        }
    }

    private void SignOutTo(GateFailure failure)
    {
        _sessionService.ClearSession();
        ResetInstalls();
        IsAuthorized = false;
        IsGlobalAdmin = false;
        UserName = null;
        UserHandle = null;
        Role = null;
        AvatarUri = null;
        _guildId = null;
        SetGuilds([], null);
        StatusMessage = null;
        IsSignedIn = false;
        Failure = failure;
        PropagateAuthorized();
    }

    private void ResetInstalls()
    {
        _recheckTimer?.Stop();
        DisposeInstalls();
        _status.Clear();
        _releases.Clear();
        _features.Clear();
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
