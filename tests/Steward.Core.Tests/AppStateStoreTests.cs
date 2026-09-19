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
        var saved = new AppState(
            new Dictionary<string, string> { ["hoobiscripts"] = "unstable" },
            new Dictionary<string, InstalledAddonRecord> { ["flavour|hoobiscripts"] = new("1.0.0", "unstable", "abc123") },
            "encrypted-token");
        store.Save(saved);

        var loaded = store.Load();

        Assert.Equal(saved.Channels, loaded.Channels);
        Assert.Equal(saved.Installs, loaded.Installs);
        Assert.Equal(saved.EncryptedSessionToken, loaded.EncryptedSessionToken);
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
    public void Load_KeepInTray_DefaultsTrue_AndRoundTrips()
    {
        File.WriteAllText(StatePath, """{"channels":{},"installs":{}}""");
        var store = new AppStateStore(["hoobiscripts"], StatePath);

        Assert.True(store.Load().KeepInTray);

        store.Save(store.Load() with { KeepInTray = false });

        Assert.False(store.Load().KeepInTray);
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
    public void Load_CorruptFile_ReturnsEmptyState()
    {
        File.WriteAllText(StatePath, """{"channels":{"hoobiscripts":"be""");

        var state = new AppStateStore(["hoobiscripts"], StatePath).Load();

        Assert.Empty(state.Channels);
        Assert.Empty(state.Installs);
        Assert.Empty(state.AddedInstalls);
    }
}
