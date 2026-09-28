using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

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
    private readonly IReadOnlyList<string> _excludedFolders;
    private readonly Func<CancellationToken, Task<bool>> _ensureAuthorized;
    private readonly Func<string, bool> _hasFeature;
    private readonly Action<string> _changeChannelRequested;
    private readonly Func<string, string, string, Task<bool>> _confirmUninstall;
    private readonly Func<WowInstall, Task> _afterStewardInstalled;

    private CancellationTokenSource? _watchCts;

    public WowInstallViewModel(
        WowInstall install,
        string? gameVersionName,
        string? userLabel,
        IReadOnlyList<ManagedAddon> addons,
        IReadOnlyList<string> excludedFolders,
        AddonUpdater updater,
        AppStateStore stateStore,
        Func<CancellationToken, Task<bool>> ensureAuthorized,
        Func<string, bool> hasFeature,
        Action<string> changeChannelRequested,
        Func<string, string, string, Task<bool>> confirmUninstall,
        Action<WowInstallViewModel> remove,
        Action<WowInstallViewModel> clientExited,
        Func<WowInstall, Task> afterStewardInstalled)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(addons);

        Install = install;
        GameVersionName = gameVersionName is null ? null : ShortProductName(gameVersionName);
        UserLabel = string.IsNullOrWhiteSpace(userLabel) ? null : userLabel;
        _excludedFolders = excludedFolders;
        _remove = remove;
        _clientExited = clientExited;
        _updater = updater;
        _stateStore = stateStore;
        _ensureAuthorized = ensureAuthorized;
        _hasFeature = hasFeature;
        _changeChannelRequested = changeChannelRequested;
        _confirmUninstall = confirmUninstall;
        _afterStewardInstalled = afterStewardInstalled;

        SyncAddons(addons);
        RescanLocal();
    }

    public event EventHandler? RowsChanged;

    public WowInstall Install { get; }

    public string DisplayName => Install.DisplayName;

    public string FlavourPath => Install.FlavourPath;

    public string AddOnsPath => Install.AddOnsPath;

    public string? ClientVersion => Install.ClientVersion;

    public string? GameVersionName { get; }

    public string? UserLabel { get; }

    public bool HasGameVersion => GameVersionName is not null;

    public ImageSource? ProductIcon => HasGameVersion ? Services.ProductIcon.For(Install.ProductCode) : null;

    public Visibility ProductIconVisibility => When(ProductIcon is not null);

    public string Label => UserLabel ?? GameVersionName ?? Install.Flavour;

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
    [NotifyPropertyChangedFor(nameof(IsClientRunning), nameof(RunningDotVisibility))]
    public partial WowClientProcess? Client { get; set; }

    public bool IsClientRunning => Client is not null;

    public Visibility RunningDotVisibility => When(IsClientRunning);

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

        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RescanLocal()
    {
        var scanned = HasGameVersion ? LocalAddons.Scan(AddOnsPath, _excludedFolders) : [];
        var hidden = _stateStore.Load().HiddenAddons.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = scanned.Select(addon =>
            LocalRows.FirstOrDefault(row => row.Matches(addon))
            ?? new LocalAddonRowViewModel(Install, addon, _stateStore, OutOfDateTip(addon.Interface), ConfirmUninstallAsync, OnLocalRemoved))
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

        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task<bool> ConfirmUninstallAsync(string name, string folders) => _confirmUninstall(name, folders, Label);

    private void OnLocalRemoved(LocalAddonRowViewModel row) => RescanLocal();

    private void OnLocalRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalAddonRowViewModel.IsHidden))
        {
            RowsChanged?.Invoke(this, EventArgs.Empty);
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
            OutOfDateTip,
            _afterStewardInstalled);
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
            RescanLocal();
            return;
        }

        RowsChanged?.Invoke(this, EventArgs.Empty);
    }
}
