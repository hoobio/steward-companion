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
}
