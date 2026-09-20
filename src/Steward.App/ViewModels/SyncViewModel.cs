using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Steward.App.Services;
using Steward.Core;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Steward.App.ViewModels;

public sealed partial class SyncViewModel : ObservableObject
{
    private static readonly string[] SummaryNames =
    [
        nameof(ChangesToSend),
        nameof(HasWaiting),
        nameof(BannerBrush),
        nameof(BannerGlyph),
        nameof(BannerTitle),
        nameof(BannerDetail),
        nameof(BannerVisibility),
        nameof(CardsVisibility),
        nameof(AddonMissingVisibility),
        nameof(NeverExportedVisibility),
        nameof(ClientRunningBannerVisibility),
        nameof(ClientRunningDetail),
        nameof(LastSyncedText),
        nameof(GeneratedFilePath),
        nameof(GeneratedFileDescription),
        nameof(MemberInfoBarIsOpen),
        nameof(MemberInfoBarText),
    ];

    private readonly MainViewModel _main;
    private readonly IGuildSyncApi _api;
    private readonly InMemoryGuildSyncApi? _fake;

    private SyncScenario? _demonstrated;
    private DateTimeOffset? _lastSyncedAt;
    private bool _isReloading;

    public SyncViewModel(MainViewModel main, IGuildSyncApi api)
    {
        _main = main;
        _api = api;
        _fake = api as InMemoryGuildSyncApi;
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

    public int ChangesToSend => Datasets
        .Where(dataset => dataset.State == SyncDatasetState.WaitingToSend)
        .Sum(dataset => dataset.NewCount);

    public bool HasWaiting => Datasets.Any(dataset => dataset.State == SyncDatasetState.WaitingToSend);

    public bool IsAnyDatasetBusy => Datasets.Any(dataset => dataset.IsSending);

    public Brush BannerBrush => (Brush)Application.Current.Resources[
        ChangesToSend > 0 ? "InfoTintBrush" : "SuccessTintBrush"];

    public string BannerGlyph => ChangesToSend > 0 ? "" : "";

    public string BannerTitle => ChangesToSend > 0
        ? $"{ChangesToSend} change{(ChangesToSend == 1 ? "" : "s")} to send"
        : "Everything is in sync";

    public string BannerDetail
    {
        get
        {
            var waiting = Datasets
                .Where(dataset => dataset.State == SyncDatasetState.WaitingToSend)
                .Select(dataset => dataset.Name.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return waiting.Count == 0
                ? $"{Datasets.Sum(dataset => dataset.LocalCount)} records across {Installs.Count} installs"
                : string.Join(", ", waiting);
        }
    }

    public Visibility BannerVisibility => When(Installs.Count > 0 && CardsVisibility == Visibility.Visible);

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

    public string LastSyncedText => $"Last synced {Relative(_lastSyncedAt)}";

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
                : $"{GeneratedFilePath}, not written yet. Writes now, read in game after /reload.";
        }
    }

    public bool MemberInfoBarIsOpen => _main.IsSignedIn && !_main.IsAuthorized;

    public string MemberInfoBarText =>
        $"Signed in as {_main.UserName}. Sending guild records needs an officer role on the guild panel. "
        + "You can still pull the guild view into the game.";

    public bool UnreachableIsOpen => IsUnreachable;

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

            _lastSyncedAt = server?.LastSyncedAt;
            Installs.Clear();
            foreach (var install in _main.Installs)
            {
                Installs.Add(Build(install, server));
            }

            Recompute();
            await DemonstrateAsync().ConfigureAwait(true);
        }
        finally
        {
            _isReloading = false;
        }
    }

    public async Task WriteGeneratedFileAsync(WowInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        GeneratedFileError = null;
        try
        {
            var payload = await _api.PullAsync(CancellationToken.None).ConfigureAwait(true);
            StewardSyncFile.Write(install.AddOnsPath, payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            GeneratedFileError = ex.Message;
        }

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

        var isSample = false;
        if (_fake is not null && Total(snapshot) == 0)
        {
            snapshot = InMemoryGuildSyncApi.LocalSample;
            isSample = true;
        }

        var isStale = SavedVariablesFreshness.Judge(snapshot, install.Client) == Freshness.Stale;
        var exportedAt = Relative(SavedVariablesFreshness.LastWrite(snapshot));

        var view = new SyncInstallViewModel
        {
            DisplayName = install.DisplayName,
            FlavourPath = install.FlavourPath,
            ClientVersion = install.ClientVersion,
            IsClientRunning = install.IsClientRunning,
            IsSample = isSample,
            AddonMissing = addonMissing,
            ReadError = readError,
        };

        view.Datasets.Add(Dataset(
            InMemoryGuildSyncApi.RosterDataset,
            "Roster",
            "members",
            "",
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
            "",
            snapshot?.Loot.Count ?? 0,
            server,
            snapshot,
            exportedAt,
            isStale,
            isFirst: false));
        view.Datasets.Add(Dataset(
            InMemoryGuildSyncApi.AttendanceDataset,
            "Attendance",
            "raids",
            "",
            snapshot?.Attendance.Count ?? 0,
            server,
            snapshot,
            exportedAt,
            isStale,
            isFirst: false));

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
        bool isFirst)
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

    private static int Total(SavedVariablesSnapshot? snapshot) =>
        (snapshot?.Roster.Count ?? 0) + (snapshot?.Loot.Count ?? 0) + (snapshot?.Attendance.Count ?? 0);

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

    private async Task DemonstrateAsync()
    {
        if (_fake is null
            || _demonstrated == _fake.Scenario
            || _fake.Scenario is not (SyncScenario.Slow or SyncScenario.DatasetFailure))
        {
            _demonstrated = _fake?.Scenario;
            return;
        }

        _demonstrated = _fake.Scenario;
        foreach (var dataset in Datasets.ToList())
        {
            if (dataset.State == SyncDatasetState.WaitingToSend)
            {
                await SendAsync(dataset).ConfigureAwait(true);
            }
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        foreach (var dataset in Datasets.ToList())
        {
            if (dataset.State == SyncDatasetState.WaitingToSend)
            {
                await SendAsync(dataset).ConfigureAwait(true);
            }
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private async Task WriteAgainAsync()
    {
        if (_main.Installs.Count > 0)
        {
            await WriteGeneratedFileAsync(_main.Installs[0].Install).ConfigureAwait(true);
        }
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
