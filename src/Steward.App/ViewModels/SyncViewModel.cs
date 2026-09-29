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
    private const string ProfessionsDatasetKey = "professions";
    private const string GuildRosterDatasetKey = "guild-roster";
    private const string GuildProfessionsDatasetKey = "guild-professions";
    private const string StewardSyncFileName = "StewardSync.lua";

    private const string GuildlessNote = "Log in to a character in a guild.";
    private const string NoSyncFeatureNote = "Your account can't push characters (missing the sync feature).";
    private const string OutdatedProfessionsNote = "Update the Steward addon: some characters' professions are in an old format and were not sent.";

    private static readonly string[] SummaryNames =
    [
        nameof(HasWaiting),
        nameof(NoInstallVisibility),
        nameof(CardsVisibility),
        nameof(CardVisibility),
        nameof(Current),
        nameof(AddonMissingVisibility),
        nameof(NeverExportedVisibility),
        nameof(SubtitleGuild),
        nameof(SubtitleSynced),
        nameof(SubtitleRunningVisibility),
        nameof(GeneratedFilePath),
        nameof(GeneratedFileStatus),
        nameof(GeneratedFileErrorVisibility),
        nameof(GeneratedFileHairline),
        nameof(SyncNowVisibility),
        nameof(IsProfessionsOnlySync),
    ];

    private readonly MainViewModel _main;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<string, SavedVariablesWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _expansionChoices = new(StringComparer.OrdinalIgnoreCase);

    private static BitmapImage? _professionsIcon;
    private static BitmapImage? _guildProfessionsIcon;
    private static BitmapImage? _guildRosterIcon;

    private bool _isReloading;

    private static BitmapImage ProfessionsIcon => _professionsIcon ??= Asset("Professions.png");

    private static BitmapImage GuildProfessionsIcon => _guildProfessionsIcon ??= Asset("GuildProfessions.png");

    private static BitmapImage GuildRosterIcon => _guildRosterIcon ??= Asset("GuildRoster.png");

    private static BitmapImage Asset(string fileName) => new(new Uri($"ms-appx:///Assets/{fileName}"));

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
            else if (e.PropertyName == nameof(MainViewModel.SelectedInstall))
            {
                _ = ReloadAsync();
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedGuild))
            {
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
    [NotifyPropertyChangedFor(nameof(GeneratedFileErrorVisibility))]
    public partial string? GeneratedFileError { get; set; }

    public bool IsProfessionsOnlySync => _main.IsProfessionsOnlySync;


    public bool HasWaiting => _main.HasSyncFeature && Installs.Any(install =>
        install.ExportState == SyncExportState.Ready
        && install.Datasets.FirstOrDefault(dataset => dataset.Key == ProfessionsDatasetKey) is { } pushRow
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

    public Visibility CardVisibility => When(_main.Installs.Count > 0);

    public SyncInstallViewModel? Current => Installs.FirstOrDefault();

    public string SubtitleGuild => _main.SelectedGuild?.Label ?? "";

    public string SubtitleSynced
    {
        get
        {
            var latest = _main.CharacterSyncRows.Max(row => row.PushedAt);
            return latest is null ? ""
                : SubtitleGuild.Length > 0 ? $" · last synced {Relative(latest)}"
                : $"Last synced {Relative(latest)}";
        }
    }

    public Visibility SubtitleRunningVisibility => When(_main.SelectedInstall is { IsClientRunning: true });

    public string GeneratedFilePath => _main.SelectedInstall is { } install
        ? StewardSyncFile.PathFor(install.AddOnsPath)
        : "";

    public string GeneratedFileStatus => File.Exists(GeneratedFilePath)
        ? $" · written {Relative(File.GetLastWriteTime(GeneratedFilePath))}"
        : " · not written yet, readable in game after /reload";

    public Visibility GeneratedFileErrorVisibility => When(GeneratedFileError is not null);

    public Thickness GeneratedFileHairline => CardsVisibility == Visibility.Visible ? new Thickness(0, 1, 0, 0) : default;

    public bool UnreachableIsOpen => IsUnreachable;

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

            var fresh = _main.Installs.Where(install => install == _main.SelectedInstall).Select(Build).ToList();
            if (fresh.Select(view => view.Shape).SequenceEqual(Installs.Select(view => view.Shape)))
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
            var pushRow = install.Datasets.FirstOrDefault(dataset => dataset.Key == ProfessionsDatasetKey);
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
            pushRow.Note = pushRow.CharacterSync is null
                ? NoSyncFeatureNote
                : install.OutdatedProfessions > 0 ? OutdatedProfessionsNote : null;
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
            DisplayName = install.Label,
            FlavourPath = install.FlavourPath,
            ClientVersion = install.ClientVersion,
            IsClientRunning = install.IsClientRunning,
            AddonMissing = addonMissing,
            ExportState = snapshot?.ExportState ?? SyncExportState.NoFile,
            ReadError = readError,
            OutdatedProfessions = snapshot?.OutdatedProfessions ?? 0,
        };

        var professionsOnly = _main.IsProfessionsOnlySync;
        IReadOnlyList<CharacterObservation> covered = snapshot is null ? []
            : professionsOnly ? CharacterSyncMapping.FilterToProfessionsOnly(snapshot)
            : snapshot.Characters;
        var outcomes = _main.GetCharacterOutcomes(install.FlavourPath);
        var batchCurrent = !professionsOnly && _main.IsCharacterPushCurrent(install.FlavourPath, snapshot?.CharactersFingerprint);
        var coveredCharacters = covered
            .Select(c => (Observation: c, View: Character(c, snapshot!.Professions, outcomes, professionsOnly, batchCurrent)))
            .ToList();
        var rosterCharacters = _main.LastDirectory?.Characters;
        var myUserId = _main.UserId;
        var shown = professionsOnly
            ? coveredCharacters
            : coveredCharacters.Where(c => CharacterSyncMapping.IsOwnCharacter(c.Observation, rosterCharacters, myUserId)).ToList();
        foreach (var character in shown
            .Select(c => c.View)
            .OrderBy(c => c.State)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            view.ProfessionsCharacters.Add(character);
        }

        var synced = coveredCharacters.Count(c => c.View.State == ProfessionsCharacterState.Synced);
        var pending = coveredCharacters.Count(c => c.View.State == ProfessionsCharacterState.Pending);
        var rejected = coveredCharacters.Count(c => c.View.State == ProfessionsCharacterState.Rejected);
        var notLinked = covered.Count(c => outcomes.GetValueOrDefault(c.CharacterGuid) is { Accepted: false, Reason: CharacterSyncRejectionCopy.NotLinkedReason });
        view.HasNoOwnCharacters = !professionsOnly && covered.Count > 0 && shown.Count == 0;
        var professions = Dataset(
            ProfessionsDatasetKey,
            "Your characters",
            professionsOnly ? "characters" : batchCurrent ? "guild characters synced" : "guild characters, sending shortly",
            covered.Count,
            sourceFile,
            exportedAt,
            isStale,
            isFirst: true,
            isSynced: synced > 0 && pending == 0);
        professions.IconSource = ProfessionsIcon;
        professions.StatusText = ProfessionsStatus(synced, pending, notLinked, rejected - notLinked);
        view.Datasets.Add(professions);
        view.ProfessionsDataset = professions;
        view.ApplyExpansion(_expansionChoices.GetValueOrDefault(install.FlavourPath));
        view.ExpansionChosen = (flavourPath, expanded) => _expansionChoices[flavourPath] = expanded;

        var written = _main.IsGuildDataWritten(install.FlavourPath, install.AddOnsPath);
        var directory = _main.LastDirectory;
        var officerPayload = _main.HasStewardFeature ? _main.LastOfficerPayload : null;
        if (_main.HasRosterFeature || _main.HasStewardFeature)
        {
            var guildRoster = Dataset(
                GuildRosterDatasetKey,
                "Guild roster",
                $"people, {directory?.Characters?.Count ?? 0} characters",
                directory?.People?.Count ?? officerPayload?.Members.Count ?? 0,
                StewardSyncFileName,
                "",
                false,
                isFirst: true);
            guildRoster.IconSource = GuildRosterIcon;
            AddPullDataset(view, guildRoster, directory?.People is not null || officerPayload is not null, written);
        }

        if (_main.HasProfessionsFeature || _main.HasStewardFeature)
        {
            var guildProfessions = Dataset(
                GuildProfessionsDatasetKey,
                "Guild professions",
                $"characters, {_main.LastMemberCatalogue?.Count ?? officerPayload?.Catalogue.Count ?? 0} professions",
                directory?.Professions?.Count ?? 0,
                StewardSyncFileName,
                "",
                false,
                isFirst: view.PullDatasets.Count == 0);
            guildProfessions.IconSource = GuildProfessionsIcon;
            AddPullDataset(view, guildProfessions, directory?.Professions is not null || officerPayload?.Catalogue.Count > 0, written);
        }

        return view;
    }

    private ProfessionsCharacterViewModel Character(
        CharacterObservation character,
        IReadOnlyDictionary<string, CharacterProfessions> professions,
        IReadOnlyDictionary<string, CharacterPushOutcome> outcomes,
        bool professionsOnly,
        bool batchCurrent)
    {
        var outcome = outcomes.GetValueOrDefault(character.CharacterGuid);
        var characterProfessions = professions.GetValueOrDefault(character.CharacterGuid);
        var state = professionsOnly
            ? ProfessionsPushSelection.StateOf(outcome, CharacterSyncMapping.ProfessionsFingerprint(characterProfessions!))
            : !batchCurrent || outcome is null ? ProfessionsCharacterState.Pending
            : outcome.Accepted ? ProfessionsCharacterState.Synced
            : ProfessionsCharacterState.Rejected;
        return new ProfessionsCharacterViewModel(
            character.Name,
            character.Level,
            WowClasses.NameFor(character.ClassId),
            ProfessionsSkillSummary.Format(characterProfessions?.Skills),
            state,
            state == ProfessionsCharacterState.Rejected && outcome?.Reason is { } reason
                ? _main.DescribeRejection(character.CharacterGuid, reason)
                : null);
    }

    private static void AddPullDataset(SyncInstallViewModel view, SyncDatasetViewModel dataset, bool pulled, bool written)
    {
        dataset.IsSynced = pulled && written;
        dataset.StatusText = !pulled ? "Not pulled yet" : written ? "In game after /reload" : "Not written to the game yet";
        view.Datasets.Add(dataset);
        view.PullDatasets.Add(dataset);
    }

    private static string? ProfessionsStatus(int synced, int pending, int notLinked, int otherRejected)
    {
        if (pending == 0 && notLinked == 0 && otherRejected == 0)
        {
            return synced == 0 ? null : "In sync with the guild";
        }

        string?[] parts =
        [
            synced > 0 ? $"{synced} synced" : null,
            pending > 0 ? $"{Characters(pending)} not sent yet" : null,
            notLinked > 0 ? $"{Characters(notLinked)} linked to other accounts" : null,
            otherRejected > 0 ? $"{Characters(otherRejected)} not accepted" : null,
        ];
        return string.Join(", ", parts.OfType<string>());
    }

    private static string Characters(int count) => $"{count} character{(count == 1 ? "" : "s")}";

    private static SyncDatasetViewModel Dataset(
        string key,
        string name,
        string unit,
        int localCount,
        string sourceFile,
        string exportedAt,
        bool isStale,
        bool isFirst,
        bool isSynced = false) => new()
    {
        Key = key,
        Name = name,
        Unit = unit,
        SourceFile = sourceFile,
        LocalCount = localCount,
        ExportedAtText = exportedAt,
        IsStale = isStale,
        IsFirst = isFirst,
        IsSynced = isSynced,
    };

    private static string SourceFile(SavedVariablesSnapshot? snapshot) => snapshot switch
    {
        null or { Files.Count: 0 } => "no saved variables file",
        { Files.Count: 1 } => Path.GetFileName(snapshot.Files[0].Path),
        _ => $"{snapshot.Files.Count} files",
    };

    internal static string Relative(DateTimeOffset? moment)
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
        if (!IsProfessionsOnlySync)
        {
            await WriteGeneratedFileAsync().ConfigureAwait(true);
        }

        await ReloadAsync().ConfigureAwait(true);
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
        GeneratedFileError = await _main.RewriteGuildDataAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
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
