using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;
using Steward.Core.Diagnostics;

using Steward.App.Services;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using Windows.Storage.Pickers;

namespace Steward.App.ViewModels;

public sealed record GameVersionOption(string Code, string Name, ImageSource? Icon)
{
    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

    public override string ToString() => Name;
}

public sealed partial class MainViewModel
{
    private const int FilterAll = 0;
    private const int FilterUpdates = 1;
    private const int FilterHidden = 2;
    private const int MaxConcurrentUpdates = 4;

    private static readonly TimeSpan CheckStaleAfter = TimeSpan.FromMinutes(5);

    private bool _lastCheckFailed;

    private static readonly string[] TableNames =
    [
        nameof(CheckedText),
        nameof(CheckedStaleVisibility),
        nameof(SelectedUpdateCount),
        nameof(TotalUpdateCount),
        nameof(UpdatesSegmentLabel),
        nameof(UpdatesBadgeVisibility),
        nameof(HiddenSegmentLabel),
        nameof(HiddenCountText),
        nameof(HiddenSegmentVisibility),
        nameof(UpdateAllVisibility),
        nameof(AdoptAllVisibility),
        nameof(AdoptAllAccessibleName),
        nameof(UpdateAllOnInstallLabel),
        nameof(SelectedUpdateCountText),
        nameof(TotalUpdateCountText),
        nameof(UpdateAllOnInstallAccessibleName),
        nameof(UpdateAllInstallsAccessibleName),
        nameof(NoMatchVisibility),
        nameof(TableVisibility),
        nameof(GameVersionPromptVisibility),
        nameof(UpdateAllEnabled),
        nameof(GetAddonsVisibility),
        nameof(MissingInstallVisibility),
        nameof(MissingInstallBody),
    ];

    private string? _sortKey;
    private bool _sortDescending;
    private readonly Dictionary<IAddonTableRow, (int Status, DateTimeOffset? Updated)> _sortSnapshot = [];

    public ObservableCollection<IAddonTableRow> TableRows { get; } = [];

    public IReadOnlyList<GameVersionOption> GameVersions =>
        [.. _supportedProducts.Select(product => new GameVersionOption(product.Key, WowInstallViewModel.ShortProductName(product.Value), ProductIcon.For(product.Key)))];

    public Func<AddonChannelViewModel, Task>? ShowChannelDialog { get; set; }

    public event EventHandler? EditInstallRequested;

    private string? _pendingEditInstall;

    public Func<string, string, string, Task<bool>>? ShowConfirmDialog { get; set; }

    [ObservableProperty]
    public partial WowInstallViewModel? SelectedInstall { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    [ObservableProperty]
    public partial int FilterIndex { get; set; }

    public string CheckedText => (_lastCheckFailed, _lastPass == default) switch
    {
        (true, true) => "Check failed",
        (true, false) => $"Check failed, last checked {LastCheckedRelative}",
        (false, true) => "Not checked yet",
        _ => $"Checked {LastCheckedRelative}",
    };

    public Visibility CheckedStaleVisibility => When(_lastCheckFailed || (_lastPass != default && DateTimeOffset.Now - _lastPass >= CheckStaleAfter));

    public int SelectedUpdateCount => SelectedInstall?.UpdateCount ?? 0;

    public int TotalUpdateCount => Installs.Sum(install => install.UpdateCount);

    private int HiddenCount => SelectedInstall?.HiddenCount ?? 0;

    public string UpdatesSegmentLabel => SelectedUpdateCount > 0 ? $"Updates {SelectedUpdateCount}" : "Updates";

    public Visibility UpdatesBadgeVisibility => When(SelectedUpdateCount > 0);

    public string HiddenSegmentLabel => $"Hidden {HiddenCount}";

    public string HiddenCountText => $"{HiddenCount}";

    public Visibility HiddenSegmentVisibility => When(HiddenCount > 0);

    public Visibility UpdateAllVisibility => When(SelectedAdoptableCount == 0 && TotalUpdateCount > 0 && TableVisibility == Visibility.Visible);

    private int SelectedAdoptableCount => HasCurseForgeFeature && SelectedInstall is { } install ? install.AdoptableRows.Count : 0;

    public Visibility AdoptAllVisibility => When(SelectedAdoptableCount > 0 && TableVisibility == Visibility.Visible);

    public string AdoptAllAccessibleName => $"Adopt all, {SelectedAdoptableCount} CurseForge match{(SelectedAdoptableCount == 1 ? "" : "es")}";

    [RelayCommand]
    private void AdoptAll() => SelectedInstall?.AdoptAll();

    [RelayCommand]
    private void AdoptNone() => SelectedInstall?.KeepAllLocal();

    public string UpdateAllOnInstallLabel => $"Update all on {SelectedInstall?.Label}";

    public string SelectedUpdateCountText => $"{SelectedUpdateCount}";

    public string TotalUpdateCountText => $"{TotalUpdateCount}";

    public string UpdateAllOnInstallAccessibleName => $"{UpdateAllOnInstallLabel}, {Updates(SelectedUpdateCount)}";

    public string UpdateAllInstallsAccessibleName => $"Update all installs, {Updates(TotalUpdateCount)}";

    private static string Updates(int count) => $"{count} update{(count == 1 ? "" : "s")}";

    public Visibility TableVisibility => When(SelectedInstall is { HasGameVersion: true, IsMissing: false });

    public Visibility GameVersionPromptVisibility => When(SelectedInstall is { HasGameVersion: false, IsMissing: false });

    public Visibility NoMatchVisibility => When(SelectedInstall is { HasGameVersion: true, IsMissing: false } && TableRows.Count == 0);

    public Visibility DefaultOrderVisibility => When(_sortKey is not null && TableVisibility == Visibility.Visible);

    public string NameSortGlyph => SortGlyph("name");

    public string SourceSortGlyph => SortGlyph("source");

    public string StatusSortGlyph => SortGlyph("status");

    public string VersionSortGlyph => SortGlyph("version");

    private string SortGlyph(string key) => _sortKey != key ? "" : _sortDescending ? "" : "";

    partial void OnSelectedInstallChanged(WowInstallViewModel? value)
    {
        foreach (var install in Installs)
        {
            install.IsSelected = install == value;
        }

        if (value is not null)
        {
            _stateStore.Save(_stateStore.Load() with { SelectedInstall = value.FlavourPath });
        }

        _sortSnapshot.Clear();
        RecomputeSummary();
    }

    partial void OnFilterTextChanged(string value) => Resort();

    partial void OnFilterIndexChanged(int value) => Resort();

    private void Resort()
    {
        _sortSnapshot.Clear();
        RecomputeTable();
    }

    private (int Status, DateTimeOffset? Updated) SortSnapshot(IAddonTableRow row) =>
        _sortSnapshot.TryGetValue(row, out var key) ? key : _sortSnapshot[row] = (row.StatusRank, row.LastUpdated);

    public void SelectInstall(WowInstallViewModel install) => SelectedInstall = install;

    private void EnsureSelection()
    {
        if (SelectedInstall is not null && Installs.Contains(SelectedInstall))
        {
            return;
        }

        var stored = _stateStore.Load().SelectedInstall;
        SelectedInstall = Installs.FirstOrDefault(install => string.Equals(install.FlavourPath, stored, StringComparison.OrdinalIgnoreCase))
            ?? Installs.FirstOrDefault();
    }

    private void RecomputeTable()
    {
        if (FilterIndex == FilterHidden && HiddenCount == 0)
        {
            FilterIndex = FilterAll;
            return;
        }

        IEnumerable<IAddonTableRow> rows = SelectedInstall is { HasGameVersion: true, IsMissing: false } install ? install.TableRows : [];
        rows = FilterIndex switch
        {
            FilterHidden => rows.Where(row => row.IsHidden),
            FilterUpdates => rows.Where(row => row.IsPendingUpdate),
            _ => rows.Where(row => !row.IsHidden),
        };

        var query = FilterText.Trim();
        if (query.Length > 0)
        {
            rows = rows.Where(row => row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var byName = StringComparer.OrdinalIgnoreCase;
        rows = (_sortKey, _sortDescending) switch
        {
            (null, _) => rows.OrderBy(row => IsConfigured(row) ? 0 : 1).ThenBy(row => IsConfigured(row) ? "" : row.DisplayName, byName),
            ("status", false) => rows.OrderBy(row => SortSnapshot(row).Status).ThenBy(row => row.DisplayName, byName),
            ("status", true) => rows.OrderByDescending(row => SortSnapshot(row).Status).ThenBy(row => row.DisplayName, byName),
            ("version", false) => rows.OrderBy(row => SortSnapshot(row).Updated is null).ThenByDescending(row => SortSnapshot(row).Updated).ThenBy(row => row.DisplayName, byName),
            ("version", true) => rows.OrderBy(row => SortSnapshot(row).Updated is null).ThenBy(row => SortSnapshot(row).Updated).ThenBy(row => row.DisplayName, byName),
            ("source", false) => rows.OrderBy(row => row.Source, byName).ThenBy(row => row.DisplayName, byName),
            ("source", true) => rows.OrderByDescending(row => row.Source, byName).ThenByDescending(row => row.DisplayName, byName),
            (_, false) => rows.OrderBy(row => row.DisplayName, byName),
            (_, true) => rows.OrderByDescending(row => row.DisplayName, byName),
        };

        var wanted = rows.ToList();
        foreach (var gone in TableRows.Except(wanted).ToList())
        {
            TableRows.Remove(gone);
        }

        for (var index = 0; index < wanted.Count; index++)
        {
            var current = TableRows.IndexOf(wanted[index]);
            if (current < 0)
            {
                TableRows.Insert(index, wanted[index]);
            }
            else if (current != index)
            {
                TableRows.Move(current, index);
            }
        }

        foreach (var name in TableNames)
        {
            OnPropertyChanged(name);
        }

        OnPropertyChanged(nameof(DefaultOrderVisibility));
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(SourceSortGlyph));
        OnPropertyChanged(nameof(StatusSortGlyph));
        OnPropertyChanged(nameof(VersionSortGlyph));
        UpdateAllCommand.NotifyCanExecuteChanged();
        UpdateAllInstallsCommand.NotifyCanExecuteChanged();
    }

    private bool IsConfigured(IAddonTableRow row) =>
        row is AddonRowViewModel addon && (_addonCatalogue ?? _addons).Any(configured => string.Equals(configured.Id, addon.AddonId, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void SortBy(string key)
    {
        (_sortKey, _sortDescending) = _sortKey != key ? (key, false) : !_sortDescending ? (key, true) : (null, false);
        Resort();
    }

    private Dictionary<string, double>? _columnWidths;

    private Dictionary<string, double> ColumnWidths => _columnWidths ??= _stateStore.Load().TableColumnWidths;

    public double? ColumnWidth(string id) => ColumnWidths.TryGetValue(id, out var width) ? width : null;

    public void SetColumnWidth(string id, double? width)
    {
        if (width is { } pixels)
        {
            ColumnWidths[id] = Math.Round(pixels);
        }
        else
        {
            ColumnWidths.Remove(id);
        }
    }

    public void WarnUi(string message) => _logger.Warn(null, message);

    private void RenumberInstalls()
    {
        var names = InstallNames.Resolve(Installs.Select(install => (install.UserLabel, install.DefaultName)));
        foreach (var (install, name) in Installs.Zip(names))
        {
            install.NumberedName = name;
        }
    }
    public void SaveColumnWidths() =>
        _stateStore.Save(_stateStore.Load() with { TableColumnWidths = new Dictionary<string, double>(ColumnWidths, StringComparer.OrdinalIgnoreCase) });

    [RelayCommand]
    private void DefaultOrder()
    {
        _sortKey = null;
        _sortDescending = false;
        Resort();
    }

    private bool CanUpdateAll => IsAuthorized && SelectedUpdateCount > 0 && !IsAnyRowBusy;

    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private Task UpdateAllAsync() => UpdatePendingAsync(SelectedInstall is { } install ? [install] : []);

    private bool CanUpdateAllInstalls => IsAuthorized && TotalUpdateCount > 0 && !IsAnyRowBusy;

    public bool UpdateAllEnabled => CanUpdateAllInstalls;

    [RelayCommand(CanExecute = nameof(CanUpdateAllInstalls))]
    private Task UpdateAllInstallsAsync() => UpdatePendingAsync([.. Installs]);

    private async Task UpdatePendingAsync(IReadOnlyList<WowInstallViewModel> installs)
    {
        await RunBoundedAsync(installs.SelectMany(install => install.TopRows).Where(row => row.IsPendingUpdate).ToList()
            .Select(row => (Func<Task>)(() => row.UpdateCommand.CanExecute(null) ? row.UpdateCommand.ExecuteAsync(null) : Task.CompletedTask)))
            .ConfigureAwait(true);

        RecomputeSummary();
    }

    private static async Task RunBoundedAsync(IEnumerable<Func<Task>> updates)
    {
        using var slots = new SemaphoreSlim(MaxConcurrentUpdates);
        await Task.WhenAll(updates.Select(async update =>
        {
            await slots.WaitAsync().ConfigureAwait(true);
            try
            {
                await update().ConfigureAwait(true);
            }
            finally
            {
                slots.Release();
            }
        })).ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenSettings() => NavigateToPageTag?.Invoke("settings");

    [RelayCommand]
    private void EditInstall()
    {
        if (SelectedInstall is { } install)
        {
            RequestEditInstall(install);
        }
    }

    public void RequestEditInstall(WowInstallViewModel install)
    {
        ArgumentNullException.ThrowIfNull(install);
        _pendingEditInstall = install.FlavourPath;
        NavigateToPageTag?.Invoke("settings");
        EditInstallRequested?.Invoke(this, EventArgs.Empty);
    }

    public WowInstallViewModel? TakeEditInstallRequest()
    {
        var path = _pendingEditInstall;
        _pendingEditInstall = null;
        return Installs.FirstOrDefault(install => string.Equals(install.FlavourPath, path, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowChannelDialogFor(string addonId)
    {
        if (AddonChannels.FirstOrDefault(channel => string.Equals(channel.AddonId, addonId, StringComparison.OrdinalIgnoreCase)) is not { } channel
            || ShowChannelDialog is not { } show)
        {
            _logger.Warn(null, $"Channel dialog not shown for {addonId}: {AddonChannels.Count} channel entries ({string.Join(", ", AddonChannels.Select(c => c.AddonId))}), dialog host {(ShowChannelDialog is null ? "missing" : "set")}");
            return;
        }

        var installed = Installs.Count(install => install.AddonRows.Any(row =>
            string.Equals(row.AddonId, addonId, StringComparison.OrdinalIgnoreCase) && row.IsInstalled));
        var scope = installed switch
        {
            <= 1 => "this install",
            2 => "both installs",
            var n => $"all {n} installs",
        };
        var when = AppStateStore.ParseAutoUpdate(_stateStore.Load().AutoUpdate) switch
        {
            AutoUpdateMode.Never => "The new version installs when you click Switch on the row.",
            AutoUpdateMode.Always => "The new version installs within a minute.",
            _ => "The new version installs once the game is closed, or when you click Switch on the row.",
        };
        channel.Hint = $"Applies to {channel.Name} on {scope}. {when}";
        _logger.Info($"Channel dialog requested for {addonId}");
        _ = ShowChannelDialogLoggedAsync(show, channel);
    }

    private async Task ShowChannelDialogLoggedAsync(Func<AddonChannelViewModel, Task> show, AddonChannelViewModel channel)
    {
        try
        {
            await show(channel).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, $"Channel dialog failed for {channel.AddonId}");
        }
    }

    private async Task<bool> ConfirmUninstallAsync(string name, string folders, string installLabel) =>
        ShowConfirmDialog is { } show
        && await show($"Uninstall {name}?", $"This deletes {folders} from {installLabel}'s AddOns folder.", "Uninstall").ConfigureAwait(true);

    private IReadOnlyList<WowInstall> DiscoverInstalls(AppState state) =>
    [
        .. WowInstalls.Discover(_supportedProducts, state.InstallProducts),
        .. state.AddedInstalls
            .Select(path => WowInstalls.FromFlavourPath(path, _supportedProducts, state.InstallProducts)
                ?? (_missingInstalls.Contains(path) ? WowInstalls.MissingFromPath(path, _supportedProducts, state.InstallProducts) : null))
            .OfType<WowInstall>(),
    ];

    public string? DetectedProduct(string flavourPath) => WowInstalls.FromFlavourPath(flavourPath, _supportedProducts)?.ProductCode;

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerWindowHandle);
        picker.FileTypeFilter.Add("*");
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public string? ValidateInstallFolder(string path, WowInstallViewModel? editing)
    {
        var install = WowInstalls.FromFlavourPath(path, _supportedProducts);
        if (install is null)
        {
            return $"{path} is not a valid WoW flavour directory.";
        }

        if (install.ProductCode is not null && !_supportedProducts.ContainsKey(install.ProductCode))
        {
            return "Steward supports World of Warcraft: Forever only.";
        }

        return Installs.Any(existing => existing != editing && string.Equals(existing.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase))
            ? $"{install.FlavourPath} is already in the install list."
            : null;
    }

    public static bool IsStructuralEdit(WowInstallViewModel install, string? productCode, string flavourPath)
    {
        ArgumentNullException.ThrowIfNull(install);
        return !string.Equals(flavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(productCode, install.Install.ProductCode, StringComparison.Ordinal);
    }

    public void SaveInstallEdit(WowInstallViewModel install, string? label, string productCode, string flavourPath)
    {
        ArgumentNullException.ThrowIfNull(install);

        var structural = IsStructuralEdit(install, productCode, flavourPath);
        if (structural && install.IsAnyRowBusy)
        {
            throw new InvalidOperationException($"An update is running on {install.Label}; the game version and folder cannot change until it finishes.");
        }

        var state = _stateStore.Load();
        if (!string.Equals(flavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase))
        {
            state = AppStateStore.RemoveInstall(state, install.FlavourPath);
            if (!state.AddedInstalls.Contains(flavourPath, StringComparer.OrdinalIgnoreCase))
            {
                state.AddedInstalls.Add(flavourPath);
            }
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            state.InstallLabels.Remove(flavourPath);
        }
        else
        {
            state.InstallLabels[flavourPath] = label.Trim();
        }

        if (string.Equals(DetectedProduct(flavourPath), productCode, StringComparison.Ordinal))
        {
            state.InstallProducts.Remove(flavourPath);
        }
        else
        {
            state.InstallProducts[flavourPath] = productCode;
        }

        _stateStore.Save(state with { SelectedInstall = flavourPath });

        if (!structural)
        {
            install.UserLabel = state.InstallLabels.GetValueOrDefault(flavourPath);
            RenumberInstalls();
            RecomputeSummary();
            return;
        }

        if (WowInstalls.FromFlavourPath(flavourPath, _supportedProducts, state.InstallProducts) is not { } rebuilt)
        {
            return;
        }

        var index = Installs.IndexOf(install);
        DetachInstall(install);
        var replacement = AddInstall(rebuilt, state.AddedInstalls.Contains(flavourPath, StringComparer.OrdinalIgnoreCase));
        Installs.Move(Installs.IndexOf(replacement), Math.Clamp(index, 0, Installs.Count - 1));
        replacement.ApplyStatus(_status, background: false);
        SelectedInstall = replacement;
        RecomputeSummary();
    }
}
