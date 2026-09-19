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

    private readonly ISessionService _sessionService;
    private readonly GigagrugClient _gigagrugClient;
    private readonly AddonUpdater _addonUpdater;
    private readonly AppStateStore _stateStore;
    private readonly IReadOnlyList<ManagedAddon> _addons;

    private DispatcherQueueTimer? _recheckTimer;

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

    public ObservableCollection<string> AvailableChannels { get; } = ["stable", "beta"];

    public nint OwnerWindowHandle { get; set; }

    [ObservableProperty]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessageVisibility))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsAuthorized { get; set; }

    [ObservableProperty]
    public partial WowInstallViewModel? SelectedInstall { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

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
                var viewModel = CreateInstallViewModel(install);
                await viewModel.RefreshAvailableAsync(CancellationToken.None);
                Installs.Add(viewModel);
            }

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
        await viewModel.RefreshAvailableAsync(CancellationToken.None);
        Installs.Add(viewModel);
        SelectedInstall = viewModel;
    }

    private WowInstallViewModel CreateInstallViewModel(WowInstall install)
    {
        var viewModel = new WowInstallViewModel(install, _addons, _addonUpdater, _stateStore, EnsureAuthorizedForActionAsync);
        viewModel.SetIsAdmin(IsAuthorized);
        return viewModel;
    }

    private void StartRecheckTimer()
    {
        _recheckTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _recheckTimer.Interval = RecheckInterval;
        _recheckTimer.IsRepeating = true;
        _recheckTimer.Tick += (_, _) => _ = RecheckAuthorizationAsync(CancellationToken.None);
        _recheckTimer.Start();
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
            if (GigagrugClient.IsGlobalAdmin(me) && !AvailableChannels.Contains("unstable"))
            {
                AvailableChannels.Add("unstable");
            }

            PropagateAuthorized();
            return IsAuthorized ? AuthCheckResult.Authorized : AuthCheckResult.NotAuthorized;
        }
        catch (SessionExpiredException)
        {
            _sessionService.ClearSession();
            IsAuthorized = false;
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
