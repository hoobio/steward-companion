using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Xaml;

namespace Steward.App.ViewModels;

public sealed partial class SyncViewModel : ObservableObject
{
    private static readonly string[] SummaryNames =
    [
        nameof(HasWaiting),
        nameof(CardsVisibility),
        nameof(AddonMissingVisibility),
        nameof(NeverExportedVisibility),
        nameof(ClientRunningBannerVisibility),
        nameof(ClientRunningDetail),
        nameof(LastSyncedText),
        nameof(GeneratedFilePath),
        nameof(GeneratedFileDescription),
        nameof(SyncNowVisibility),
    ];

    private readonly MainViewModel _main;
    private readonly IGuildSyncApi _api;

    private bool _isReloading;

    public SyncViewModel(MainViewModel main, IGuildSyncApi api)
    {
        _main = main;
        _api = api;
        _main.CharacterSyncRows.CollectionChanged += (_, _) =>
        {
            SyncCharacterPushRows();
            Recompute();
        };
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.HasSyncFeature))
            {
                SyncCharacterPushRows();
                Recompute();
            }
        };
    }

    public MainViewModel Main => _main;

    public Action? StateChanged { get; set; }

    public ObservableCollection<SyncInstallViewModel> Installs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreachableIsOpen))]
    public partial bool IsUnreachable { get; set; }

    [ObservableProperty]
    public partial string? GeneratedFileError { get; set; }

    public IEnumerable<SyncDatasetViewModel> Datasets => Installs.SelectMany(install => install.Datasets);

    public bool HasWaiting => Datasets
        .Where(dataset => !dataset.IsComingSoon)
        .Any(dataset => dataset.State == SyncDatasetState.WaitingToSend);

    public Visibility CardsVisibility =>
        When(AddonMissingVisibility == Visibility.Collapsed && NeverExportedVisibility == Visibility.Collapsed);

    public Visibility AddonMissingVisibility =>
        When(Installs.Count > 0 && Installs.All(install => install.AddonMissing));

    public Visibility NeverExportedVisibility => When(
        Installs.Count > 0
        && !Installs.All(install => install.AddonMissing)
        && Datasets.All(dataset => dataset.LocalCount == 0));

    public Visibility ClientRunningBannerVisibility => When(Installs.Any(install => install.IsClientRunning));

    public string ClientRunningDetail
    {
        get
        {
            var exported = Datasets.Select(dataset => dataset.ExportedAtText).FirstOrDefault() ?? "earlier";
            return $"This data was exported {exported}, before your current session. "
                + "Log out or /reload in game to export again.";
        }
    }

    public string LastSyncedText
    {
        get
        {
            var latest = _main.CharacterSyncRows.Max(row => row.PushedAt);
            return latest is null ? string.Empty : $"Last synced {Relative(latest)}";
        }
    }

    public string GeneratedFilePath => _main.Installs.Count == 0
        ? "No World of Warcraft install found"
        : StewardSyncFile.PathFor(_main.Installs[0].AddOnsPath);

    public string GeneratedFileDescription
    {
        get
        {
            if (GeneratedFileError is { } error)
            {
                return error;
            }

            return File.Exists(GeneratedFilePath)
                ? $"{GeneratedFilePath}, written {Relative(File.GetLastWriteTime(GeneratedFilePath))}"
                : $"{GeneratedFilePath}, not written yet. Readable in game after /reload.";
        }
    }

    public bool UnreachableIsOpen => IsUnreachable;

    public Visibility SyncNowVisibility => When(_main.HasSyncFeature);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public async Task ReloadAsync()
    {
        if (_isReloading)
        {
            return;
        }

        _isReloading = true;
        try
        {
            SyncServerState? server = null;
            IsUnreachable = !_main.IsApiReachable;
            if (!IsUnreachable)
            {
                try
                {
                    server = await _api.GetStateAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (HttpRequestException)
                {
                    IsUnreachable = true;
                }
            }

            var fresh = _main.Installs.Select(install => Build(install, server)).ToList();
            if (fresh.Select(view => view.FlavourPath).SequenceEqual(Installs.Select(view => view.FlavourPath)))
            {
                for (var i = 0; i < fresh.Count; i++)
                {
                    Installs[i].CopyFrom(fresh[i]);
                }
            }
            else
            {
                Installs.Clear();
                foreach (var view in fresh)
                {
                    Installs.Add(view);
                }
            }

            SyncCharacterPushRows();
            Recompute();
        }
        finally
        {
            _isReloading = false;
        }
    }

    private void SyncCharacterPushRows()
    {
        foreach (var install in Installs)
        {
            var roster = install.Datasets.FirstOrDefault(dataset => dataset.Key == InMemoryGuildSyncApi.RosterDataset);
            if (roster is null)
            {
                continue;
            }

            roster.CharacterSync = _main.HasSyncFeature
                ? _main.CharacterSyncRows.FirstOrDefault(row => row.FlavourPath == install.FlavourPath)
                : null;
        }
    }

    public async Task WriteGeneratedFileAsync()
    {
        GeneratedFileError = await _main.SyncRosterAsync().ConfigureAwait(true);
        Recompute();
    }

    private SyncInstallViewModel Build(WowInstallViewModel install, SyncServerState? server)
    {
        var addonMissing = !Directory.Exists(
            Path.Combine(install.AddOnsPath, StewardSavedVariables.AddonName));

        SavedVariablesSnapshot? snapshot = null;
        string? readError = null;
        try
        {
            snapshot = StewardSavedVariables.Read(install.FlavourPath);
        }
        catch (FormatException ex)
        {
            readError = ex.Message;
        }

        var isStale = SavedVariablesFreshness.Judge(snapshot, install.Client) == Freshness.Stale;
        var exportedAt = Relative(SavedVariablesFreshness.LastWrite(snapshot));

        var view = new SyncInstallViewModel
        {
            DisplayName = install.DisplayName,
            FlavourPath = install.FlavourPath,
            ClientVersion = install.ClientVersion,
            IsClientRunning = install.IsClientRunning,
            AddonMissing = addonMissing,
            ReadError = readError,
        };

        view.Datasets.Add(Dataset(
            InMemoryGuildSyncApi.RosterDataset,
            "Roster",
            "members",
            "",
            snapshot?.Roster.Count ?? 0,
            server,
            snapshot,
            exportedAt,
            isStale,
            isFirst: true));
        view.Datasets.Add(Dataset(
            InMemoryGuildSyncApi.LootDataset,
            "Loot",
            "loot events",
            "",
            snapshot?.Loot.Count ?? 0,
            server,
            snapshot,
            exportedAt,
            isStale,
            isFirst: false,
            isComingSoon: true));
        view.Datasets.Add(Dataset(
            InMemoryGuildSyncApi.AttendanceDataset,
            "Attendance",
            "raids",
            "",
            snapshot?.Attendance.Count ?? 0,
            server,
            snapshot,
            exportedAt,
            isStale,
            isFirst: false,
            isComingSoon: true));

        return view;
    }

    private SyncDatasetViewModel Dataset(
        string key,
        string name,
        string unit,
        string glyph,
        int localCount,
        SyncServerState? server,
        SavedVariablesSnapshot? snapshot,
        string exportedAt,
        bool isStale,
        bool isFirst,
        bool isComingSoon = false)
    {
        var dataset = new SyncDatasetViewModel
        {
            Key = key,
            Name = name,
            Unit = unit,
            Glyph = glyph,
            SourceFile = SourceFile(snapshot),
            LocalCount = localCount,
            ServerCount = server?.RecordCounts.GetValueOrDefault(key) ?? localCount,
            ExportedAtText = exportedAt,
            IsStale = isStale,
            IsFirst = isFirst,
            IsComingSoon = isComingSoon,
            Payload = PayloadFor(key, snapshot),
            Send = SendAsync,
        };

        dataset.IsAdmin = _main.IsAuthorized;
        dataset.IsBlocked = IsUnreachable;
        return dataset;
    }

    private static SyncPayload PayloadFor(string key, SavedVariablesSnapshot? snapshot) => new(
        DateTimeOffset.Now,
        snapshot?.ExportedAt,
        key == InMemoryGuildSyncApi.RosterDataset ? snapshot?.Roster ?? [] : [],
        key == InMemoryGuildSyncApi.LootDataset ? snapshot?.Loot ?? [] : [],
        key == InMemoryGuildSyncApi.AttendanceDataset ? snapshot?.Attendance ?? [] : [],
        [],
        [],
        []);

    private static string SourceFile(SavedVariablesSnapshot? snapshot) => snapshot switch
    {
        null or { Files.Count: 0 } => "no saved variables file",
        { Files.Count: 1 } => Path.GetFileName(snapshot.Files[0].Path),
        _ => $"{snapshot.Files.Count} files",
    };

    private static string Relative(DateTimeOffset? moment)
    {
        if (moment is null)
        {
            return "never";
        }

        var elapsed = DateTimeOffset.Now - moment.Value;
        return true switch
        {
            _ when elapsed < TimeSpan.FromMinutes(1) => "just now",
            _ when elapsed < TimeSpan.FromHours(1) =>
                $"{(int)elapsed.TotalMinutes} minute{((int)elapsed.TotalMinutes == 1 ? "" : "s")} ago",
            _ when elapsed < TimeSpan.FromDays(1) =>
                $"{(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours == 1 ? "" : "s")} ago",
            _ => $"{(int)elapsed.TotalDays} day{((int)elapsed.TotalDays == 1 ? "" : "s")} ago",
        };
    }

    private async Task SendAsync(SyncDatasetViewModel dataset)
    {
        dataset.Error = null;
        dataset.Progress = 0;
        dataset.IsSending = true;
        try
        {
            var progress = new Progress<double>(value => dataset.Progress = value);
            var result = await _api.PushAsync(dataset.Key, dataset.Payload, progress, CancellationToken.None)
                .ConfigureAwait(true);
            if (!result.Accepted)
            {
                dataset.Error = result.Error;
            }
        }
        catch (HttpRequestException ex)
        {
            dataset.Error = ex.Message;
            IsUnreachable = true;
        }
        finally
        {
            dataset.IsSending = false;
            Recompute();
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await _main.PushCharacterSyncAsync(force: true).ConfigureAwait(true);
        await WriteGeneratedFileAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // AsyncRelayCommand never reports IsRunning for a task that completes synchronously, which ReloadAsync does against local files.
        await Task.Yield();
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task WriteAgainAsync()
    {
        await WriteGeneratedFileAsync().ConfigureAwait(true);
    }

    private void Recompute()
    {
        foreach (var name in SummaryNames)
        {
            OnPropertyChanged(name);
        }

        StateChanged?.Invoke();
    }
}
