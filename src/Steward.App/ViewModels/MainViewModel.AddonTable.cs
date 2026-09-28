using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

using Microsoft.UI.Xaml;

using Windows.Storage.Pickers;

namespace Steward.App.ViewModels;

public sealed record GameVersionOption(string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class MainViewModel
{
    private const int FilterAll = 0;
    private const int FilterUpdates = 1;
    private const int FilterHidden = 2;

    private static readonly string[] TableNames =
    [
        nameof(CheckedText),
        nameof(SelectedUpdateCount),
        nameof(TotalUpdateCount),
        nameof(UpdatesSegmentLabel),
        nameof(HiddenSegmentLabel),
        nameof(HiddenSegmentVisibility),
        nameof(UpdateAllLabel),
        nameof(UpdateAllOnInstallLabel),
        nameof(SelectedUpdateCountText),
        nameof(TotalUpdateCountText),
        nameof(UpdateAllOnInstallAccessibleName),
        nameof(UpdateAllInstallsAccessibleName),
        nameof(NoMatchVisibility),
        nameof(TableVisibility),
        nameof(GameVersionPromptVisibility),
        nameof(InstallPickerVisibility),
        nameof(UpdateAllEnabled),
    ];

    private string? _sortKey;
    private bool _sortDescending;

    public ObservableCollection<IAddonTableRow> TableRows { get; } = [];

    public IReadOnlyList<GameVersionOption> GameVersions =>
        [.. _supportedProducts.Select(product => new GameVersionOption(product.Key, WowInstallViewModel.ShortProductName(product.Value)))];

    public Func<AddonChannelViewModel, Task>? ShowChannelDialog { get; set; }

    public Func<WowInstallViewModel, Task>? ShowEditInstallDialog { get; set; }

    public Func<string, string, string, Task<bool>>? ShowConfirmDialog { get; set; }

    [ObservableProperty]
    public partial WowInstallViewModel? SelectedInstall { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    [ObservableProperty]
    public partial int FilterIndex { get; set; }

    public string CheckedText => _lastPass == default ? "Not checked yet" : $"Checked {LastCheckedRelative}";

    public int SelectedUpdateCount => SelectedInstall?.UpdateCount ?? 0;

    public int TotalUpdateCount => Installs.Sum(install => install.UpdateCount);

    private int HiddenCount => SelectedInstall?.HiddenCount ?? 0;

    public string UpdatesSegmentLabel => SelectedUpdateCount > 0 ? $"Updates {SelectedUpdateCount}" : "Updates";

    public string HiddenSegmentLabel => $"Hidden {HiddenCount}";

    public Visibility HiddenSegmentVisibility => When(HiddenCount > 0);

    public string UpdateAllLabel => SelectedUpdateCount > 0 ? $"Update all ({SelectedUpdateCount})" : "Update all";

    public string UpdateAllOnInstallLabel => $"Update all on {SelectedInstall?.Label}";

    public string SelectedUpdateCountText => $"{SelectedUpdateCount}";

    public string TotalUpdateCountText => $"{TotalUpdateCount}";

    public string UpdateAllOnInstallAccessibleName => $"{UpdateAllOnInstallLabel}, {Updates(SelectedUpdateCount)}";

    public string UpdateAllInstallsAccessibleName => $"Update all installs, {Updates(TotalUpdateCount)}";

    private static string Updates(int count) => $"{count} update{(count == 1 ? "" : "s")}";

    public Visibility InstallPickerVisibility => When(SelectedInstall is not null);

    public Visibility TableVisibility => When(SelectedInstall is { HasGameVersion: true });

    public Visibility GameVersionPromptVisibility => When(SelectedInstall is { HasGameVersion: false });

    public Visibility NoMatchVisibility => When(SelectedInstall is { HasGameVersion: true } && TableRows.Count == 0);

    public Visibility DefaultOrderVisibility => When(_sortKey is not null);

    public string NameSortGlyph => SortGlyph("name");

    public string SourceSortGlyph => SortGlyph("source");

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

        RecomputeSummary();
    }

    partial void OnFilterTextChanged(string value) => RecomputeTable();

    partial void OnFilterIndexChanged(int value) => RecomputeTable();

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

        IEnumerable<IAddonTableRow> rows = SelectedInstall is { HasGameVersion: true } install ? install.TableRows : [];
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

        if (_sortKey is not null)
        {
            Func<IAddonTableRow, string> key = _sortKey == "source" ? row => row.Source : row => row.DisplayName;
            rows = _sortDescending
                ? rows.OrderByDescending(key, StringComparer.OrdinalIgnoreCase).ThenByDescending(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(key, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

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
        UpdateAllCommand.NotifyCanExecuteChanged();
        UpdateAllInstallsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SortBy(string key)
    {
        (_sortKey, _sortDescending) = _sortKey != key ? (key, false) : !_sortDescending ? (key, true) : (null, false);
        RecomputeTable();
    }

    [RelayCommand]
    private void DefaultOrder()
    {
        _sortKey = null;
        _sortDescending = false;
        RecomputeTable();
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
        foreach (var row in installs.SelectMany(install => install.AddonRows).Where(row => row.IsPendingUpdate).ToList())
        {
            if (row.UpdateCommand.CanExecute(null))
            {
                await row.UpdateCommand.ExecuteAsync(null).ConfigureAwait(true);
            }
        }

        RecomputeSummary();
    }

    [RelayCommand]
    private void OpenSettings() => NavigateToPageTag?.Invoke("settings");

    [RelayCommand]
    private async Task EditInstallAsync()
    {
        if (SelectedInstall is { } install && ShowEditInstallDialog is { } show)
        {
            await show(install).ConfigureAwait(true);
        }
    }

    private void ShowChannelDialogFor(string addonId)
    {
        if (AddonChannels.FirstOrDefault(channel => string.Equals(channel.AddonId, addonId, StringComparison.OrdinalIgnoreCase)) is not { } channel
            || ShowChannelDialog is not { } show)
        {
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
        channel.Hint = $"Applies to {channel.Name} on {scope}. The new version installs when you click Switch on the row.";
        _ = show(channel);
    }

    private async Task<bool> ConfirmUninstallAsync(string name, string folders, string installLabel) =>
        ShowConfirmDialog is { } show
        && await show($"Uninstall {name}?", $"This deletes {folders} from {installLabel}'s AddOns folder.", "Uninstall").ConfigureAwait(true);

    private IReadOnlyList<WowInstall> DiscoverInstalls(AppState state) =>
    [
        .. WowInstalls.Discover(_supportedProducts, state.InstallProducts),
        .. state.AddedInstalls.Select(path => WowInstalls.FromFlavourPath(path, _supportedProducts, state.InstallProducts)).OfType<WowInstall>(),
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

    public void SaveInstallEdit(WowInstallViewModel install, string? label, string productCode, string flavourPath)
    {
        ArgumentNullException.ThrowIfNull(install);

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
