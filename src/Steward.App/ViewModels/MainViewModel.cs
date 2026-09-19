using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using Windows.Storage.Pickers;

namespace Steward.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly ISessionService _sessionService;
    private readonly GigagrugClient _gigagrugClient;
    private readonly AddonUpdater _addonUpdater;
    private readonly AppStateStore _stateStore;
    private readonly IReadOnlyList<ManagedAddon> _addons;
    private readonly Dictionary<string, AddonChannelStatus> _status = new(StringComparer.OrdinalIgnoreCase);

    private DispatcherQueueTimer? _recheckTimer;
    private DateTimeOffset _lastPass;
    private bool _isChecking;

    public MainViewModel(
        ISessionService sessionService,
        GigagrugClient gigagrugClient,
        AddonUpdater addonUpdater,
        AppStateStore stateStore,
        IReadOnlyList<ManagedAddon> addons)
    {
        _sessionService = sessionService;
        _gigagrugClient = gigagrugClient;
        _addonUpdater = addonUpdater;
        _stateStore = stateStore;
        _addons = addons;
    }

    public ObservableCollection<WowInstallViewModel> Installs { get; } = [];

    public nint OwnerWindowHandle { get; set; }

    [ObservableProperty]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessageVisibility))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsAuthorized { get; set; }

    [ObservableProperty]
    public partial bool IsGlobalAdmin { get; set; }

    [ObservableProperty]
    public partial WowInstallViewModel? SelectedInstall { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string LastCheckedText { get; set; } = "Not checked yet";

    private IReadOnlyList<string> VisibleChannels => IsGlobalAdmin ? AddonChannelStatus.Ordered : ["stable", "beta"];

    public Visibility StatusMessageVisibility =>
        string.IsNullOrEmpty(StatusMessage) ? Visibility.Collapsed : Visibility.Visible;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            if (!_sessionService.TryRestoreSession())
            {
                StatusMessage = "Signing in...";
                await _sessionService.SignInAsync(CancellationToken.None);
            }

            var result = await RecheckAuthorizationAsync(CancellationToken.None);
            if (result == AuthCheckResult.SessionExpired)
            {
                StatusMessage = "Signing in...";
                await _sessionService.SignInAsync(CancellationToken.None);
                await RecheckAuthorizationAsync(CancellationToken.None);
            }

            foreach (var install in WowInstalls.Discover())
            {
                Installs.Add(CreateInstallViewModel(install));
            }

            await CheckAsync(background: false, CancellationToken.None).ConfigureAwait(true);

            SelectedInstall ??= Installs.FirstOrDefault();
            StartRecheckTimer();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
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

        var install = WowInstalls.FromFlavourPath(folder.Path);
        if (install is null)
        {
            StatusMessage = $"{folder.Path} is not a valid WoW flavour directory.";
            return;
        }

        var viewModel = CreateInstallViewModel(install);
        Installs.Add(viewModel);
        viewModel.ApplyStatus(_status, background: false);
        SelectedInstall = viewModel;
    }

    private async Task CheckAsync(bool background, CancellationToken cancellationToken)
    {
        try
        {
            var state = _stateStore.Load();
            foreach (var addon in _addons)
            {
                _status[addon.Id] = AddonChannelStatus.Resolve(
                    state.Channels.GetValueOrDefault(addon.Id),
                    await _addonUpdater.ProbeChannelsAsync(addon, VisibleChannels, cancellationToken).ConfigureAwait(true));
            }
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = ex.Message;
            return;
        }

        foreach (var install in Installs)
        {
            install.ApplyStatus(_status, background);
        }

        _lastPass = DateTimeOffset.Now;
        UpdateLastCheckedText();
    }

    private WowInstallViewModel CreateInstallViewModel(WowInstall install)
    {
        var viewModel = new WowInstallViewModel(install, _addons, _addonUpdater, _stateStore, EnsureAuthorizedForActionAsync);
        viewModel.SetIsAdmin(IsAuthorized);
        return viewModel;
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
        if (!_isChecking && DateTimeOffset.Now - _lastPass >= RecheckInterval)
        {
            _ = RunBackgroundPassAsync();
        }
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
        finally
        {
            _isChecking = false;
        }
    }

    private void UpdateLastCheckedText()
    {
        var elapsed = DateTimeOffset.Now - _lastPass;
        LastCheckedText = true switch
        {
            _ when _lastPass == default => "Not checked yet",
            _ when elapsed < TimeSpan.FromMinutes(1) => "Checked just now",
            _ when elapsed < TimeSpan.FromMinutes(60) =>
                $"Checked {(int)elapsed.TotalMinutes} minute{((int)elapsed.TotalMinutes == 1 ? "" : "s")} ago",
            _ => $"Checked {(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours == 1 ? "" : "s")} ago",
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
            IsAuthorized = GigagrugClient.IsAdmin(me);
            StatusMessage = IsAuthorized ? null : $"Signed in as {UserName}. Admin role required for updates.";

            // Client-side gate only: gigagrug does not restrict who can fetch the unstable
            // manifest, and this re-check is likewise a UI truth-teller, not enforcement.
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
            StatusMessage = "Your session has expired. Sign in again.";
            _recheckTimer?.Stop();
            PropagateAuthorized();
            return AuthCheckResult.SessionExpired;
        }
    }

    private void PropagateAuthorized()
    {
        foreach (var install in Installs)
        {
            install.SetIsAdmin(IsAuthorized);
        }
    }

    private enum AuthCheckResult
    {
        Authorized,
        NotAuthorized,
        SessionExpired,
    }
}
