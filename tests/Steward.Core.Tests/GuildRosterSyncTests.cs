namespace Steward.Core.Tests;

public sealed class GuildRosterSyncTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-roster-sync-").FullName;
    private string StatePath => Path.Combine(_root, "state.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SyncPayload SamplePayload() => new(
        DateTimeOffset.FromUnixTimeSeconds(1758270000),
        null,
        [],
        [],
        [],
        [
            new GuildRosterMember(
                "111", "Hoobi", null, "hoobi#0001", "Raider", ["EU"], ["core"], null,
                "Reliable", false, 12, 1758200000, null, null),
        ],
        [new DiscordMember("222", "Grug", null)]);

    private WowInstall InstallAddon(bool withToc = true)
    {
        var addOnsPath = Path.Combine(_root, "AddOns");
        var addonPath = Path.Combine(addOnsPath, "Steward");
        Directory.CreateDirectory(addonPath);
        if (withToc)
        {
            File.WriteAllText(Path.Combine(addonPath, "Steward.toc"), "## Interface: 11507\n");
        }

        return new WowInstall(_root, "_retail_", _root, addOnsPath, null, null, "Retail");
    }

    [Fact]
    public void WriteIfChanged_SkipsTheInstall_WhenTheAddonIsMissing()
    {
        var install = InstallAddon(withToc: false);
        var stateStore = new AppStateStore(["steward"], StatePath);

        var written = GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore);

        Assert.False(written);
        Assert.False(File.Exists(Path.Combine(install.AddOnsPath, "Steward", "StewardSync.lua")));
    }

    [Fact]
    public void WriteIfChanged_DoesNotRewrite_WhenTheRosterIsUnchanged()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var target = Path.Combine(install.AddOnsPath, "Steward", "StewardSync.lua");

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
        Assert.True(File.Exists(target));
        File.Delete(target);

        var written = GuildRosterSync.WriteIfChanged(install, SamplePayload() with { WrittenAt = DateTimeOffset.FromUnixTimeSeconds(1758280000) }, stateStore);

        Assert.False(written);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void WriteIfChanged_Rewrites_WhenTheRosterChanges()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));

        var changed = SamplePayload() with { Discord = [new DiscordMember("222", "Grug", "grugnick")] };
        Assert.True(GuildRosterSync.WriteIfChanged(install, changed, stateStore));
    }
}
