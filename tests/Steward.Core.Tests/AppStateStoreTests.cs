namespace Steward.Core.Tests;

public sealed class AppStateStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-state-").FullName;
    private string StatePath => Path.Combine(_root, "state.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Load_SeedsEveryConfiguredAddon_FromLegacyChannel()
    {
        File.WriteAllText(StatePath, """{"channel":"stable","installs":{}}""");

        var state = new AppStateStore(["hoobiscripts", "steward"], StatePath).Load();

        Assert.Equal("stable", state.Channels["hoobiscripts"]);
        Assert.Equal("stable", state.Channels["steward"]);
        Assert.Null(state.LegacyChannel);
    }

    [Fact]
    public void Load_MigratesAStoredDevelopmentChannel_ToPreRelease()
    {
        File.WriteAllText(StatePath, """{"channels":{"hoobiscripts":"development","steward":"release"},"installs":{}}""");

        var state = new AppStateStore(["hoobiscripts", "steward"], StatePath).Load();

        Assert.Equal("pre-release", state.Channels["hoobiscripts"]);
        Assert.Equal("release", state.Channels["steward"]);
    }

    [Fact]
    public void Load_MigratesAStoredDevelopChannel_ToPreRelease()
    {
        File.WriteAllText(StatePath, """{"channels":{"hoobiscripts":"develop","steward":"release"},"installs":{}}""");

        var state = new AppStateStore(["hoobiscripts", "steward"], StatePath).Load();

        Assert.Equal("pre-release", state.Channels["hoobiscripts"]);
        Assert.Equal("release", state.Channels["steward"]);
    }

    [Fact]
    public void Load_KeepsAnExplicitPerAddonChannel_OverTheLegacyValue()
    {
        File.WriteAllText(StatePath, """{"channel":"stable","channels":{"hoobiscripts":"unstable"},"installs":{}}""");

        var state = new AppStateStore(["hoobiscripts", "steward"], StatePath).Load();

        Assert.Equal("unstable", state.Channels["hoobiscripts"]);
        Assert.Equal("stable", state.Channels["steward"]);
    }

    [Fact]
    public void Save_AfterLoad_DoesNotWriteTheLegacyChannelKey()
    {
        File.WriteAllText(StatePath, """{"channel":"stable","installs":{}}""");
        var store = new AppStateStore(["hoobiscripts"], StatePath);

        var state = store.Load();
        store.Save(state);

        var json = File.ReadAllText(StatePath);
        Assert.Contains("\"channels\"", json);
        Assert.DoesNotContain("\"channel\":", json);
    }

    [Fact]
    public void Load_NoFile_ReturnsEmptyChannels()
    {
        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Empty(state.Channels);
        Assert.Empty(state.Installs);
    }

    [Fact]
    public void Load_NewFormatFile_RoundTrips()
    {
        var store = new AppStateStore(["hoobiscripts"], StatePath);
        var installedAt = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var saved = new AppState(
            new Dictionary<string, string> { ["hoobiscripts"] = "unstable" },
            new Dictionary<string, InstalledAddonRecord> { ["flavour|hoobiscripts"] = new("1.0.0", "unstable", "abc123", installedAt) },
            "encrypted-token");
        store.Save(saved);

        var loaded = store.Load();

        Assert.Equal(saved.Channels, loaded.Channels);
        Assert.Equal(saved.Installs, loaded.Installs);
        Assert.Equal(saved.EncryptedSessionToken, loaded.EncryptedSessionToken);
        Assert.Equal(installedAt, loaded.Installs["flavour|hoobiscripts"].InstalledAt);
    }

    [Fact]
    public void Load_InstallWithoutInstalledAt_HasNullInstalledAt()
    {
        File.WriteAllText(
            StatePath,
            """{"channels":{},"installs":{"flavour|hoobiscripts":{"version":"1.0.0","channel":"beta","sha256":"aa"}}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Null(state.Installs["flavour|hoobiscripts"].InstalledAt);
    }

    [Fact]
    public void Load_FileWithNeitherChannelKey_ReturnsEmptyChannels()
    {
        File.WriteAllText(StatePath, """{"installs":{}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Empty(state.Channels);
    }

    [Fact]
    public void Load_FileWithoutInstallsKey_ReturnsEmptyInstalls()
    {
        File.WriteAllText(StatePath, """{"channels":{"hoobiscripts":"beta"}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Empty(state.Installs);
    }

    [Fact]
    public void Load_ChannelsNullInJson_ReturnsEmptyChannels()
    {
        File.WriteAllText(StatePath, """{"channels":null,"installs":{}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Empty(state.Channels);
    }

    [Fact]
    public void Load_AddedInstalls_RoundTrips()
    {
        var store = new AppStateStore(["hoobiscripts"], StatePath);
        store.Save(new AppState([], [], null, [@"C:\wow\_retail_"]));

        var state = store.Load();

        Assert.Equal([@"C:\wow\_retail_"], state.AddedInstalls);
    }

    [Fact]
    public void Load_FileWithoutAddedInstallsKey_ReturnsEmptyAddedInstalls()
    {
        File.WriteAllText(StatePath, """{"channels":{},"installs":{}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Empty(state.AddedInstalls);
    }

    [Fact]
    public void RemoveInstall_DropsThePathAndEveryRecordUnderIt()
    {
        var state = new AppState(
            [],
            new Dictionary<string, InstalledAddonRecord>
            {
                [AppStateStore.Key(@"C:\wow\_retail_", "hoobiscripts")] = new("1.0.0", "beta", "aa"),
                [AppStateStore.Key(@"C:\wow\_retail_", "steward")] = new("2.0.0", "beta", "bb"),
                [AppStateStore.Key(@"C:\wow\_classic_era_", "hoobiscripts")] = new("1.0.0", "beta", "cc"),
            },
            null,
            [@"C:\wow\_retail_", @"C:\wow\_classic_era_"]);

        var result = AppStateStore.RemoveInstall(state, @"C:\wow\_retail_");

        Assert.Equal([@"C:\wow\_classic_era_"], result.AddedInstalls);
        Assert.Equal([AppStateStore.Key(@"C:\wow\_classic_era_", "hoobiscripts")], result.Installs.Keys);
    }

    [Fact]
    public void RemoveInstall_DropsTheRestedXpGuideRecordsAndChoiceForThatPath()
    {
        var state = new AppState([], [], null, [@"C:\wow\_retail_", @"C:\wow\_classic_era_"])
        {
            RestedXpGuides = new Dictionary<string, RestedXpGuideRecord>(StringComparer.OrdinalIgnoreCase)
            {
                [AppStateStore.Key(@"C:\wow\_retail_", "Forever Leveling Guide")] = new(1, DateTimeOffset.UnixEpoch),
                [AppStateStore.Key(@"C:\wow\_classic_era_", "Forever Leveling Guide")] = new(2, DateTimeOffset.UnixEpoch),
            },
            RestedXpGuideChoices = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [@"C:\wow\_retail_"] = ["Forever Leveling Guide"],
                [@"C:\wow\_classic_era_"] = ["Forever Leveling Guide"],
            },
            RestedXpGuidesGeneration = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                [@"C:\wow\_retail_"] = 1,
                [@"C:\wow\_classic_era_"] = 2,
            },
        };

        var result = AppStateStore.RemoveInstall(state, @"C:\wow\_retail_");

        Assert.Equal([AppStateStore.Key(@"C:\wow\_classic_era_", "Forever Leveling Guide")], result.RestedXpGuides.Keys);
        Assert.Equal([@"C:\wow\_classic_era_"], result.RestedXpGuideChoices.Keys);
        Assert.Equal([@"C:\wow\_classic_era_"], result.RestedXpGuidesGeneration.Keys);
    }

    [Fact]
    public void Load_MigratesTheSingleGuideChoiceIntoAOneItemList()
    {
        File.WriteAllText(
            StatePath,
            """{"channels":{},"installs":{},"restedxp_guide_choice":{"C:\\wow\\_classic_beta_":"Forever Leveling Guide"}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Equal(["Forever Leveling Guide"], state.RestedXpGuideChoices[@"C:\wow\_classic_beta_"]);
        Assert.Null(state.LegacyRestedXpGuideChoice);
    }

    [Fact]
    public void Load_TrayBehaviour_DefaultsToMinimizeOnly_AndRoundTrips()
    {
        File.WriteAllText(StatePath, """{"channels":{},"installs":{}}""");
        var store = new AppStateStore(["hoobiscripts"], StatePath);

        Assert.True(store.Load().MinimizeToTray);
        Assert.False(store.Load().CloseToTray);

        store.Save(store.Load() with { MinimizeToTray = false, CloseToTray = true });

        Assert.False(store.Load().MinimizeToTray);
        Assert.True(store.Load().CloseToTray);
    }

    [Fact]
    public void Load_AutoUpdate_DefaultsToOutOfGame_AndRoundTrips()
    {
        File.WriteAllText(StatePath, """{"channels":{},"installs":{}}""");
        var store = new AppStateStore(["hoobiscripts"], StatePath);

        Assert.Equal("out-of-game", store.Load().AutoUpdate);

        store.Save(store.Load() with { AutoUpdate = "always" });

        Assert.Equal("always", store.Load().AutoUpdate);
    }

    [Theory]
    [InlineData("always", AutoUpdateMode.Always)]
    [InlineData("out-of-game", AutoUpdateMode.OutOfGame)]
    [InlineData("never", AutoUpdateMode.Never)]
    [InlineData("bogus", AutoUpdateMode.OutOfGame)]
    [InlineData(null, AutoUpdateMode.OutOfGame)]
    public void ParseAutoUpdate_UnknownOrMissingValue_FallsBackToOutOfGame(string? value, AutoUpdateMode expected)
    {
        Assert.Equal(expected, AppStateStore.ParseAutoUpdate(value));
    }

    [Fact]
    public void Load_GuildId_DefaultsToNull_AndRoundTrips()
    {
        File.WriteAllText(StatePath, """{"channels":{},"installs":{}}""");
        var store = new AppStateStore(["hoobiscripts"], StatePath);

        Assert.Null(store.Load().GuildId);

        store.Save(store.Load() with { GuildId = "123456789" });

        Assert.Equal("123456789", store.Load().GuildId);
    }

    [Fact]
    public void Load_ChannelLookup_IsCaseInsensitive()
    {
        File.WriteAllText(StatePath, """{"channels":{"hoobiscripts":"beta"},"installs":{}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Equal("beta", state.Channels["HoobiScripts"]);
    }

    [Fact]
    public void Load_InstallLookup_IsCaseInsensitive()
    {
        File.WriteAllText(
            StatePath,
            """{"channels":{},"installs":{"C:\\wow\\_retail_|hoobiscripts":{"version":"1.0.0","channel":"beta","sha256":"aa"}}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Equal("1.0.0", state.Installs[AppStateStore.Key(@"c:\WOW\_RETAIL_", "HoobiScripts")].Version);
    }

    [Fact]
    public void Save_WritesAtomically_LeavesNoTempFile()
    {
        var store = new AppStateStore(["hoobiscripts"], StatePath);

        store.Save(new AppState([], []));

        Assert.True(File.Exists(StatePath));
        Assert.False(File.Exists($"{StatePath}.tmp"));
    }

    [Fact]
    public void Load_MissingHiddenAddons_IsEmptyList()
    {
        File.WriteAllText(StatePath, """{"channels":{},"installs":{}}""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Empty(state.HiddenAddons);
    }

    [Fact]
    public void Save_HiddenAddons_RoundTrips()
    {
        var store = new AppStateStore(["hoobiscripts"], StatePath);
        store.Save(new AppState([], [], null, [], true, ["restedxp"]));

        var state = store.Load();

        Assert.Equal(["restedxp"], state.HiddenAddons);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyState()
    {
        File.WriteAllText(StatePath, """{"channels":{"hoobiscripts":"be""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Empty(state.Channels);
        Assert.Empty(state.Installs);
        Assert.Empty(state.AddedInstalls);
    }
}
