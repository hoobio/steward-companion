using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public sealed partial class WowInstallViewModel : ObservableObject, IDisposable
{
    private const string ProductPrefix = "World of Warcraft: ";

    private static readonly string[] RowTriggers =
    [
        nameof(AddonRowViewModel.IsBusy),
        nameof(AddonRowViewModel.InstalledVersion),
        nameof(AddonRowViewModel.AvailableVersion),
        nameof(AddonRowViewModel.Channel),
        nameof(AddonRowViewModel.HasFailed),
        nameof(AddonRowViewModel.IsHidden),
        nameof(AddonRowViewModel.IsIgnored),
    ];

    private readonly Action<WowInstallViewModel> _remove;
    private readonly Action<WowInstallViewModel> _clientExited;
    private readonly AddonUpdater _updater;
    private readonly AppStateStore _stateStore;
    private readonly Func<WowInstallViewModel, IReadOnlyList<string>> _excludedFolders;
    private readonly Func<WowInstallViewModel, IReadOnlyList<LocalAddon>, Task<IReadOnlyList<ProviderAddonRecord>>> _identifyProviderAddons;
    private readonly Func<WowInstallViewModel, IReadOnlyList<ProviderAddonRecord>, bool> _reconcileProviderAddons;
    private readonly Func<CancellationToken, Task<bool>> _ensureAuthorized;
    private readonly Func<string, bool> _hasFeature;
    private readonly Action<string> _changeChannelRequested;
    private readonly Func<string, string, string, Task<bool>> _confirmUninstall;
    private readonly Func<WowInstall, Task> _afterStewardInstalled;
    private readonly ILogger _logger;

    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();

    private CancellationTokenSource? _watchCts;
    private int _scanGeneration;

    public WowInstallViewModel(
        WowInstall install,
        string? gameVersionName,
        string? userLabel,
        IReadOnlyList<ManagedAddon> addons,
        Func<WowInstallViewModel, IReadOnlyList<string>> excludedFolders,
        Func<WowInstallViewModel, IReadOnlyList<LocalAddon>, Task<IReadOnlyList<ProviderAddonRecord>>> identifyProviderAddons,
        Func<WowInstallViewModel, IReadOnlyList<ProviderAddonRecord>, bool> reconcileProviderAddons,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized,
        Func<string, bool> hasFeature,
        Action<string> changeChannelRequested,
        Func<string, string, string, Task<bool>> confirmUninstall,
        Action<WowInstallViewModel> remove,
        Action<WowInstallViewModel> clientExited,
        Func<WowInstall, Task> afterStewardInstalled,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(addons);

        Install = install;
        GameVersionName = gameVersionName is null ? null : ShortProductName(gameVersionName);
        UserLabel = string.IsNullOrWhiteSpace(userLabel) ? null : userLabel;
        _excludedFolders = excludedFolders;
        _identifyProviderAddons = identifyProviderAddons;
        _reconcileProviderAddons = reconcileProviderAddons;
        _remove = remove;
        _clientExited = clientExited;
        _updater = updater;
        _stateStore = stateStore;
        _ensureAuthorized = ensureAuthorized;
        _hasFeature = hasFeature;
        _changeChannelRequested = changeChannelRequested;
        _confirmUninstall = confirmUninstall;
        _afterStewardInstalled = afterStewardInstalled;

        _logger = logger;

        SyncAddons(addons);
        _ = RescanLocalAsync();
    }

    public event EventHandler? RowsChanged;

    public WowInstall Install { get; }

    public string DisplayName => Install.DisplayName;

    public string FlavourPath => Install.FlavourPath;

    public string AddOnsPath => Install.AddOnsPath;

    public string? ClientVersion => Install.ClientVersion;

    public string? GameVersionName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(EditAccessibleName), nameof(StatusText), nameof(GameVersionLineVisibility))]
    public partial string? UserLabel { get; set; }

    public bool IsAnyRowBusy => AddonRows.Any(row => row.IsBusy);

    public bool HasGameVersion => GameVersionName is not null;

    public ImageSource? ProductIcon => HasGameVersion ? Services.ProductIcon.For(Install.ProductCode) : null;

    public Visibility ProductIconVisibility => When(ProductIcon is not null);

    public string DefaultName => GameVersionName ?? Install.Flavour;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(EditAccessibleName))]
    public partial string? NumberedName { get; set; }

    public string Label => UserLabel ?? NumberedName ?? DefaultName;

    public string EditAccessibleName => $"Edit {Label}";

    public string StatusText => UserLabel is not null ? $"{GameVersionName} · {ClientVersion}" : ClientVersion ?? "";

    public Visibility StatusVisibility => When(HasGameVersion);

    public Visibility NoGameVersionVisibility => When(!HasGameVersion);

    public Visibility GameVersionLineVisibility => When(UserLabel is not null && HasGameVersion);

    public ObservableCollection<AddonRowViewModel> AddonRows { get; } = [];

    public ObservableCollection<LocalAddonRowViewModel> LocalRows { get; } = [];

    public IEnumerable<IAddonTableRow> TableRows => AddonRows.Cast<IAddonTableRow>().Concat(LocalRows);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddedByYouVisibility))]
    public partial bool IsAddedByUser { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDotVisibility))]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClientRunning), nameof(RunningText))]
    public partial WowClientProcess? Client { get; set; }

    public bool IsClientRunning => Client is not null;

    public string RunningText => IsClientRunning ? "  ● Running" : "";

    public Visibility CurrentDotVisibility => When(IsSelected);

    public int UpdateCount => AddonRows.Count(row => row.IsPendingUpdate);

    public int HiddenCount => TableRows.Count(row => row.IsHidden);

    public Visibility AddedByYouVisibility => When(IsAddedByUser);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public static string ShortProductName(string displayName) =>
        displayName.StartsWith(ProductPrefix, StringComparison.Ordinal) ? displayName[ProductPrefix.Length..] : displayName;

    public string? OutOfDateTip(string? interfaceDirective)
    {
        if (interfaceDirective is null || TocFile.InterfaceNumber(ClientVersion) is not { } client
            || TocFile.MatchesInterface(interfaceDirective, client))
        {
            return null;
        }

        var built = int.TryParse(interfaceDirective, out var single)
            ? $"{single} ({TocFile.FormatInterface(single)})"
            : interfaceDirective;
        return $"Out of date for this client. Built for interface {built}. {Label} runs {client} ({TocFile.FormatInterface(client)}). "
            + "The game skips it unless Load out of date AddOns is ticked on the AddOns screen.";
    }

    private string? OutOfDateTipForToc(string tocPath)
    {
        try
        {
            return File.Exists(tocPath) ? OutOfDateTip(TocFile.ReadDirective(tocPath, "Interface")) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn(ex, $"Could not read {tocPath} for the out of date check");
            return null;
        }
    }

    public void ApplyStatus(IReadOnlyDictionary<string, AddonChannelStatus> status, bool background)
    {
        ArgumentNullException.ThrowIfNull(status);

        foreach (var row in AddonRows)
        {
            if (background && row.IsBusy) { continue; }
            if (status.TryGetValue(row.AddonId, out var addonStatus)) { row.Apply(addonStatus); }
        }
    }

    public void RefreshClientRunning()
    {
        var client = WowClient.Find(Install);
        if (client?.ProcessId != Client?.ProcessId)
        {
            StopWatching();
            if (client is not null)
            {
                _watchCts = new CancellationTokenSource();
                _ = WatchExitAsync(client.ProcessId, _watchCts.Token);
            }
        }

        Client = client;
        foreach (var row in AddonRows)
        {
            row.IsClientRunning = IsClientRunning;
            if (row.NeedsReload && (!IsClientRunning || SavedVariablesFreshness.WrittenSince(Install.FlavourPath, row.ReloadPendingSince)))
            {
                row.NeedsReload = false;
            }
        }
    }

    public void Dispose() => StopWatching();

    public void SetIsAdmin(bool isAdmin)
    {
        foreach (var row in AddonRows)
        {
            row.IsAdmin = isAdmin;
        }
    }

    public void SyncHidden(IReadOnlySet<string> hiddenIds)
    {
        foreach (var row in TableRows)
        {
            row.IsHidden = hiddenIds.Contains(row.HiddenId);
        }
    }

    public void SyncAddons(IReadOnlyList<ManagedAddon> addons)
    {
        ArgumentNullException.ThrowIfNull(addons);

        if (!HasGameVersion)
        {
            addons = [];
        }

        var wantedIds = addons.Select(addon => addon.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in AddonRows.Where(row => !wantedIds.Contains(row.AddonId)).ToList())
        {
            gone.PropertyChanged -= OnRowPropertyChanged;
            AddonRows.Remove(gone);
        }

        for (var index = 0; index < addons.Count; index++)
        {
            var addon = addons[index];
            var existing = AddonRows.FirstOrDefault(row => string.Equals(row.AddonId, addon.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var row = CreateRow(addon);
                row.PropertyChanged += OnRowPropertyChanged;
                AddonRows.Insert(Math.Min(index, AddonRows.Count), row);
            }
            else if (AddonRows.IndexOf(existing) != index)
            {
                AddonRows.Move(AddonRows.IndexOf(existing), index);
            }
        }

        RaiseRowsChanged();
    }

    public async Task RescanLocalAsync()
    {
        var generation = Interlocked.Increment(ref _scanGeneration);
        try
        {
            var excluded = _excludedFolders(this);
            var identify = _hasFeature(GigagrugClient.CurseForgeFeature);
            var scanned = HasGameVersion
                ? await Task.Run(() => LocalAddons.Scan(AddOnsPath, excluded, _logger)).ConfigureAwait(false)
                : [];
            var identified = HasGameVersion && identify ? await _identifyProviderAddons(this, scanned).ConfigureAwait(false) : [];
            var adopted = false;
            await OnUiThreadAsync(() =>
            {
                adopted = generation == _scanGeneration && _reconcileProviderAddons(this, identified);
                excluded = _excludedFolders(this);
            }).ConfigureAwait(false);
            if (adopted)
            {
                scanned = await Task.Run(() => LocalAddons.Scan(AddOnsPath, excluded, _logger)).ConfigureAwait(false);
            }

            await OnUiThreadAsync(() => ApplyLocalScan(generation, scanned)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, $"Local addon rescan failed for {FlavourPath}");
        }
    }

    private Task OnUiThreadAsync(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource();
        if (!_dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }))
        {
            done.SetException(new InvalidOperationException("The UI thread is no longer running."));
        }

        return done.Task;
    }

    private void RaiseRowsChanged()
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            RowsChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcher.TryEnqueue(() => RowsChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    private void ApplyLocalScan(int generation, IReadOnlyList<LocalAddon> scanned)
    {
        if (generation != _scanGeneration)
        {
            return;
        }

        var hidden = _stateStore.Load().HiddenAddons.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = scanned.Select(addon =>
            LocalRows.FirstOrDefault(row => row.Matches(addon))
            ?? new LocalAddonRowViewModel(Install, addon, _stateStore, OutOfDateTip(addon.Interface), ConfirmUninstallAsync, OnLocalRemoved, _logger))
            .ToList();
        foreach (var row in wanted)
        {
            row.IsHidden = hidden.Contains(row.HiddenId);
        }

        foreach (var row in LocalRows)
        {
            row.PropertyChanged -= OnLocalRowPropertyChanged;
        }

        LocalRows.Clear();
        foreach (var row in wanted)
        {
            row.PropertyChanged += OnLocalRowPropertyChanged;
            LocalRows.Add(row);
        }

        RaiseRowsChanged();
    }

    private Task<bool> ConfirmUninstallAsync(string name, string folders) => _confirmUninstall(name, folders, Label);

    private void OnLocalRemoved(LocalAddonRowViewModel row) => _ = RescanLocalAsync();

    private void OnLocalRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalAddonRowViewModel.IsHidden))
        {
            RaiseRowsChanged();
        }
    }

    private AddonRowViewModel CreateRow(ManagedAddon addon)
    {
        var features = addon.Features;
        return new AddonRowViewModel(
            Install,
            addon,
            _updater,
            _stateStore,
            async ct => features.Any(_hasFeature) && await _ensureAuthorized(ct).ConfigureAwait(true),
            _changeChannelRequested,
            ConfirmUninstallAsync,
            OutOfDateTipForToc,
            _afterStewardInstalled,
            _logger);
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    [RelayCommand]
    private void OpenFolder() =>
        Process.Start(new ProcessStartInfo(AddOnsPath) { UseShellExecute = true })?.Dispose();

    private void StopWatching()
    {
        _watchCts?.Cancel();
        _watchCts?.Dispose();
        _watchCts = null;
    }

    private async Task WatchExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            await WowClient.WaitForExitAsync(processId, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        RefreshClientRunning();
        _clientExited(this);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || !RowTriggers.Contains(e.PropertyName))
        {
            return;
        }

        if (e.PropertyName == nameof(AddonRowViewModel.InstalledVersion))
        {
            _ = RescanLocalAsync();
            return;
        }

        RaiseRowsChanged();
    }
}
