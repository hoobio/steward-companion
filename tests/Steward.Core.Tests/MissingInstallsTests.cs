namespace Steward.Core.Tests;

public sealed class MissingInstallsTests : IDisposable
{
    private const string Gone = @"D:\wow\_retail_";
    private const string Kept = @"C:\wow\_classic_era_";
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("steward-missing-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static AppState StateWith(params string[] installs) =>
        new AppState([], [], null, [.. installs]) with { MissingSince = [] };

    private static MissingInstallReport Detect(AppState state, DateTimeOffset now, bool folderExists = false, bool driveExists = true) =>
        MissingInstalls.Detect(state, now, path => path == Kept || (folderExists && path == Gone), _ => driveExists);

    [Fact]
    public void MissingFromPath_BuildsAFlaggedInstallForAnAbsentFolder()
    {
        var products = new Dictionary<string, string> { ["wow_classic_beta"] = "World of Warcraft: Forever" };
        var overrides = new Dictionary<string, string> { [Gone] = "wow_classic_beta" };

        var install = WowInstalls.MissingFromPath(Gone, products, overrides);

        Assert.True(install.IsMissing);
        Assert.Equal(Gone, install.FlavourPath);
        Assert.Equal("wow_classic_beta", install.ProductCode);
        Assert.Equal("World of Warcraft: Forever", install.DisplayName);
    }

    [Fact]
    public void WriteIfChanged_SkipsAMissingInstall()
    {
        var install = WowInstalls.MissingFromPath(Gone, new Dictionary<string, string>());
        var payload = new SyncPayload(Now, null, [], [], [], [], [], []);

        Assert.False(GuildRosterSync.WriteIfChanged(install, payload, new AppStateStore([], Path.Combine(_root, "state.json"))));
    }

    [Fact]
    public void Detect_FolderMissing_RecordsWhenFirstSeenAndReportsIt()
    {
        var report = Detect(StateWith(Gone, Kept), Now);

        Assert.Equal([Gone], report.Missing);
        Assert.Empty(report.DueForRemoval);
        Assert.Equal(Now, report.State.MissingSince[Gone]);
        Assert.False(report.State.MissingSince.ContainsKey(Kept));
    }

    [Fact]
    public void Detect_KeepsTheFirstSeenTime_OnLaterChecks()
    {
        var first = Detect(StateWith(Gone), Now);

        var later = Detect(first.State, Now.AddDays(3));

        Assert.Equal(Now, later.State.MissingSince[Gone]);
    }

    [Fact]
    public void Detect_FolderBack_ClearsMissingSince()
    {
        var first = Detect(StateWith(Gone), Now);

        var back = Detect(first.State, Now.AddDays(1), folderExists: true);

        Assert.Empty(back.Missing);
        Assert.Empty(back.State.MissingSince);
    }

    [Fact]
    public void Detect_ThirteenDaysMissing_IsNotDue()
    {
        var first = Detect(StateWith(Gone), Now);

        var later = Detect(first.State, Now.AddDays(13));

        Assert.Equal([Gone], later.Missing);
        Assert.Empty(later.DueForRemoval);
    }

    [Fact]
    public void Detect_FourteenDaysMissing_IsDue()
    {
        var first = Detect(StateWith(Gone), Now);

        var later = Detect(first.State, Now.AddDays(14));

        Assert.Equal([Gone], later.DueForRemoval);
    }

    [Fact]
    public void Detect_DriveRootMissing_ReportsMissingButNeverDue()
    {
        var first = Detect(StateWith(Gone), Now, driveExists: false);

        var later = Detect(first.State, Now.AddDays(60), driveExists: false);

        Assert.Equal([Gone], later.Missing);
        Assert.Empty(later.DueForRemoval);
        Assert.Equal(Now, later.State.MissingSince[Gone]);
    }

    [Fact]
    public void Detect_DropsMissingSinceForAPathNoLongerAdded()
    {
        var state = StateWith(Kept) with
        {
            MissingSince = new Dictionary<string, DateTimeOffset> { [Gone] = Now },
        };

        Assert.Empty(Detect(state, Now).State.MissingSince);
    }

    [Fact]
    public void MissingSince_RoundTripsThroughTheStore()
    {
        var store = new AppStateStore([], Path.Combine(_root, "state.json"));
        store.Save(Detect(StateWith(Gone), Now).State);

        Assert.Equal(Now, store.Load().MissingSince[Gone]);
    }

    [Fact]
    public void RemoveInstall_PrunesEveryKeyAndBothSideFiles_ForThatPathOnly()
    {
        var store = new AppStateStore([], Path.Combine(_root, "state.json"));
        var guildKey = AppStateStore.CharacterSyncKey("guild", Gone);
        var keptGuildKey = AppStateStore.CharacterSyncKey("guild", Kept);
        var provider = new ProviderAddonRecord("curseforge-1-1", "Questie", "Questie", "CurseForge", 1, 1, ["Questie"]);
        var push = new CharacterPushRecord("fp", Now, 1);
        var state = StateWith(Gone, Kept) with
        {
            Channels = new Dictionary<string, string> { ["steward"] = "release" },
            Installs = new Dictionary<string, InstalledAddonRecord>
            {
                [AppStateStore.Key(Gone, "steward")] = new("1", "release", "aa"),
                [AppStateStore.Key(Kept, "steward")] = new("1", "release", "bb"),
            },
            RestedXpGuides = new Dictionary<string, RestedXpGuideRecord>
            {
                [AppStateStore.Key(Gone, "guide")] = new(1, Now),
                [AppStateStore.Key(Kept, "guide")] = new(1, Now),
            },
            RestedXpGuideChoices = new Dictionary<string, List<string>> { [Gone] = ["guide"], [Kept] = ["guide"] },
            RestedXpGuidesGeneration = new Dictionary<string, long> { [Gone] = 1, [Kept] = 1 },
            GuildRosterSync = new Dictionary<string, string> { [Gone] = "a", [Kept] = "b" },
            CharacterSync = new Dictionary<string, CharacterPushRecord> { [guildKey] = push, [keptGuildKey] = push },
            CharacterSyncBatches = new Dictionary<string, CharacterSyncBatch>
            {
                [guildKey] = new("fp", "one"),
                [keptGuildKey] = new("fp", "two"),
            },
            InstallLabels = new Dictionary<string, string> { [Gone] = "Main", [Kept] = "Alt" },
            InstallProducts = new Dictionary<string, string> { [Gone] = "wow", [Kept] = "wow_classic_era" },
            IgnoredAddons = [AppStateStore.Key(Gone, "steward"), AppStateStore.Key(Kept, "steward")],
            ProviderAddons = new Dictionary<string, List<ProviderAddonRecord>> { [Gone] = [provider], [Kept] = [provider] },
            MissingSince = new Dictionary<string, DateTimeOffset> { [Gone] = Now, [Kept] = Now },
            SelectedInstall = Gone,
        };
        store.Save(state);

        store.Save(AppStateStore.RemoveInstall(store.Load(), Gone));

        var result = store.Load();
        Assert.Equal([Kept], result.AddedInstalls);
        Assert.Equal([AppStateStore.Key(Kept, "steward")], result.Installs.Keys);
        Assert.Equal([AppStateStore.Key(Kept, "guide")], result.RestedXpGuides.Keys);
        Assert.Equal([Kept], result.RestedXpGuideChoices.Keys);
        Assert.Equal([Kept], result.RestedXpGuidesGeneration.Keys);
        Assert.Equal([Kept], result.GuildRosterSync.Keys);
        Assert.Equal([keptGuildKey], result.CharacterSync.Keys);
        Assert.Equal([keptGuildKey], result.CharacterSyncBatches.Keys);
        Assert.Equal([Kept], result.InstallLabels.Keys);
        Assert.Equal([Kept], result.InstallProducts.Keys);
        Assert.Equal([AppStateStore.Key(Kept, "steward")], result.IgnoredAddons);
        Assert.Equal([Kept], result.ProviderAddons.Keys);
        Assert.Equal([Kept], result.MissingSince.Keys);
        Assert.Null(result.SelectedInstall);
        Assert.Equal("release", result.Channels["steward"]);
        Assert.DoesNotContain(Gone.Replace(@"\", @"\\"), File.ReadAllText(Path.Combine(_root, "character_sync.json")));
        Assert.DoesNotContain(Gone.Replace(@"\", @"\\"), File.ReadAllText(Path.Combine(_root, "provider_addons.json")));
        Assert.DoesNotContain(Gone.Replace(@"\", @"\\"), File.ReadAllText(Path.Combine(_root, "state.json")));    }
}
