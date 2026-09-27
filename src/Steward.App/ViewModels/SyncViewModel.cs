using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.Core;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Steward.App.ViewModels;

public sealed partial class SyncViewModel : ObservableObject
{
    private const string RosterDatasetKey = "roster";
    private const string LootDatasetKey = "loot";
    private const string AttendanceDatasetKey = "attendance";
    private const string ProfessionsDatasetKey = "professions";

    private const string GuildlessNote = "Log in to a character in a guild.";
    private const string NoSyncFeatureNote = "Your account can't push characters (missing the sync feature).";

    private static readonly string[] SummaryNames =
    [
        nameof(HasWaiting),
        nameof(NoInstallVisibility),
        nameof(CardsVisibility),
        nameof(AddonMissingVisibility),
        nameof(NeverExportedVisibility),
        nameof(ClientRunningBannerVisibility),
        nameof(ClientRunningDetail),
        nameof(LastSyncedText),
        nameof(GeneratedFilePath),
        nameof(GeneratedFileDescription),
        nameof(GeneratedFileVisibility),
        nameof(SyncNowVisibility),
        nameof(IsProfessionsOnlySync),
        nameof(PageTitle),
        nameof(ProfessionsOnlyVisibility),
    ];

    private readonly MainViewModel _main;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<string, SavedVariablesWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);

    private bool _isReloading;

    public SyncViewModel(MainViewModel main)
    {
        _main = main;
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

    public bool IsProfessionsOnlySync => _main.IsProfessionsOnlySync;

    public string PageTitle => IsProfessionsOnlySync ? "Your professions" : "Sync";

    public Visibility ProfessionsOnlyVisibility => When(IsProfessionsOnlySync);

    public bool HasWaiting => _main.HasSyncFeature && Installs.Any(install =>
        install.ExportState == SyncExportState.Ready
        && install.Datasets.FirstOrDefault(dataset => dataset.Key is RosterDatasetKey or ProfessionsDatasetKey) is { } pushRow
        && (pushRow.CharacterSync is null || pushRow.CharacterSync.Error is not null));

    public Visibility NoInstallVisibility => When(_main.Installs.Count == 0);

    public Visibility CardsVisibility => When(
        NoInstallVisibility == Visibility.Collapsed
        && AddonMissingVisibility == Visibility.Collapsed
        && NeverExportedVisibility == Visibility.Collapsed);

    public Visibility AddonMissingVisibility =>
        When(Installs.Count > 0 && Installs.All(install => install.AddonMissing));

    public Visibility NeverExportedVisibility => When(
        Installs.Count > 0
        && !Installs.All(install => install.AddonMissing)
        && Installs.All(install => install.ReadError is null && install.ExportState == SyncExportState.NoFile));

    private IEnumerable<SyncDatasetViewModel> StaleWhileRunning =>
        Installs.Where(install => install.IsClientRunning)
            .SelectMany(install => install.Datasets)
            .Where(dataset => dataset.IsStale);

    public Visibility ClientRunningBannerVisibility => When(StaleWhileRunning.Any());

    public string ClientRunningDetail
    {
        get
        {
            var exported = StaleWhileRunning
                .Select(dataset => dataset.ExportedAtText)
                .FirstOrDefault() ?? "earlier";
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

    public Visibility GeneratedFileVisibility => When(!IsProfessionsOnlySync);

    public Visibility SyncNowVisibility => When(_main.HasSyncFeature);

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public Task ReloadAsync()
    {
        if (_isReloading)
        {
            return Task.CompletedTask;
        }

        _isReloading = true;
        try
        {
            IsUnreachable = !_main.IsApiReachable;
            WatchSavedVariables();

            var fresh = _main.Installs.Select(Build).ToList();
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
            return Task.CompletedTask;
        }
        finally
        {
            _isReloading = false;
        }
    }

    private void WatchSavedVariables()
    {
        foreach (var install in _main.Installs.Where(install => !_watchers.ContainsKey(install.FlavourPath)))
        {
            var watcher = SavedVariablesWatcher.TryCreate(
                install.FlavourPath,
                $"{StewardSavedVariables.AddonName}.lua",
                () => _dispatcher?.TryEnqueue(() => _ = _main.SavedVariablesWrittenAsync()));
            if (watcher is not null)
            {
                _watchers[install.FlavourPath] = watcher;
            }
        }
    }

    private void SyncCharacterPushRows()
    {
        foreach (var install in Installs)
        {
            var pushRow = install.Datasets.FirstOrDefault(dataset => dataset.Key is RosterDatasetKey or ProfessionsDatasetKey);
            if (pushRow is null)
            {
                continue;
            }

            if (install.ExportState == SyncExportState.Guildless)
            {
                pushRow.CharacterSync = null;
                pushRow.Note = GuildlessNote;
                continue;
            }

            pushRow.CharacterSync = _main.HasSyncFeature
                ? _main.CharacterSyncRows.FirstOrDefault(row => row.FlavourPath == install.FlavourPath)
                : null;
            pushRow.Note = pushRow.CharacterSync is null ? NoSyncFeatureNote : null;
        }
    }

    public async Task WriteGeneratedFileAsync()
    {
        GeneratedFileError = await _main.SyncRosterAsync().ConfigureAwait(true);
        Recompute();
    }

    private SyncInstallViewModel Build(WowInstallViewModel install)
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
        var sourceFile = SourceFile(snapshot);

        var view = new SyncInstallViewModel
        {
            DisplayName = install.DisplayName,
            FlavourPath = install.FlavourPath,
            ClientVersion = install.ClientVersion,
            IsClientRunning = install.IsClientRunning,
            AddonMissing = addonMissing,
            ExportState = snapshot?.ExportState ?? SyncExportState.NoFile,
            ReadError = readError,
        };

        if (_main.IsProfessionsOnlySync)
        {
            IReadOnlyList<CharacterObservation> covered = snapshot is null ? [] : CharacterSyncMapping.FilterToProfessionsOnly(snapshot);
            var outcomes = _main.GetCharacterOutcomes(install.FlavourPath);
            view.ProfessionsCharacters = [.. covered
                .Select(c => ProfessionsCharacter(c, snapshot!.Professions, outcomes))
                .OrderBy(c => c.State)
                .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];

            var synced = view.ProfessionsCharacters.Count(c => c.State == ProfessionsCharacterState.Synced);
            var pending = view.ProfessionsCharacters.Count(c => c.State == ProfessionsCharacterState.Pending);
            var rejected = view.ProfessionsCharacters.Count(c => c.State == ProfessionsCharacterState.Rejected);
            var professions = Dataset(
                ProfessionsDatasetKey,
                "Your professions",
                "characters",
                "",
                covered.Count,
                sourceFile,
                exportedAt,
                isStale,
                isFirst: true,
                isSynced: synced > 0 && pending == 0,
                showOutcomeSummary: false);
            professions.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Professions.png"));
            professions.StatusText = ProfessionsStatus(synced, pending, rejected);
            view.Datasets.Add(professions);

            return view;
        }

        view.Datasets.Add(Dataset(
            RosterDatasetKey,
            "Roster",
            "characters",
            "",
            snapshot?.Characters.Count ?? 0,
            sourceFile,
            exportedAt,
            isStale,
            isFirst: true,
            isSynced: _main.IsRosterInSync(install.FlavourPath, install.AddOnsPath, snapshot?.CharactersFingerprint)));
        view.Datasets.Add(Dataset(
            LootDatasetKey,
            "Loot",
            "loot events",
            "",
            snapshot?.Loot.Count ?? 0,
            sourceFile,
            exportedAt,
            isStale,
            isFirst: false,
            isComingSoon: true));
        view.Datasets.Add(Dataset(
            AttendanceDatasetKey,
            "Attendance",
            "raids",
            "",
            snapshot?.Attendance.Count ?? 0,
            sourceFile,
            exportedAt,
            isStale,
            isFirst: false,
            isComingSoon: true));

        return view;
    }

    private ProfessionsCharacterViewModel ProfessionsCharacter(
        CharacterObservation character,
        IReadOnlyDictionary<string, CharacterProfessions> professions,
        IReadOnlyDictionary<string, CharacterPushOutcome> outcomes)
    {
        var outcome = outcomes.GetValueOrDefault(character.CharacterGuid);
        var characterProfessions = professions[character.CharacterGuid];
        var state = ProfessionsPushSelection.StateOf(outcome, CharacterSyncMapping.ProfessionsFingerprint(characterProfessions));
        return new ProfessionsCharacterViewModel(
            character.Name,
            character.Level,
            WowClasses.NameFor(character.ClassId),
            ProfessionsSkillSummary.Format(characterProfessions.Skills),
            state,
            state == ProfessionsCharacterState.Rejected && outcome?.Reason is { } reason
                ? _main.DescribeRejection(character.CharacterGuid, reason)
                : null);
    }

    private static string? ProfessionsStatus(int synced, int pending, int rejected)
    {
        if (pending == 0 && rejected == 0)
        {
            return synced == 0 ? null : "In sync with the guild";
        }

        string?[] parts =
        [
            synced > 0 ? $"{synced} synced" : null,
            pending > 0 ? $"{Characters(pending)} not sent yet" : null,
            rejected > 0 ? $"{Characters(rejected)} not accepted" : null,
        ];
        return string.Join(", ", parts.OfType<string>());
    }

    private static string Characters(int count) => $"{count} character{(count == 1 ? "" : "s")}";

    private static SyncDatasetViewModel Dataset(
        string key,
        string name,
        string unit,
        string glyph,
        int localCount,
        string sourceFile,
        string exportedAt,
        bool isStale,
        bool isFirst,
        bool isComingSoon = false,
        bool isSynced = false,
        bool showOutcomeSummary = true) => new()
    {
        Key = key,
        Name = name,
        Unit = unit,
        Glyph = glyph,
        SourceFile = sourceFile,
        LocalCount = localCount,
        ExportedAtText = exportedAt,
        IsStale = isStale,
        IsFirst = isFirst,
        IsComingSoon = isComingSoon,
        IsSynced = isSynced,
        ShowOutcomeSummary = showOutcomeSummary,
    };

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

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await _main.PushCharacterSyncAsync(force: true).ConfigureAwait(true);
        if (IsProfessionsOnlySync)
        {
            await ReloadAsync().ConfigureAwait(true);
        }
        else
        {
            await WriteGeneratedFileAsync().ConfigureAwait(true);
        }
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
