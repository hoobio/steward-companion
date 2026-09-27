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
        [new DiscordMember("222", "Grug", null)],
        ["Officer", "Raider"]);

    private static AvatarImage SampleAvatar(string hash) =>
        new($"https://cdn.discordapp.com/avatars/1/{hash}.png?size=64", 64, 64, new byte[64 * 64 * 4]);

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
        var first = File.ReadAllText(target);

        var written = GuildRosterSync.WriteIfChanged(install, SamplePayload() with { WrittenAt = DateTimeOffset.FromUnixTimeSeconds(1758280000) }, stateStore);

        Assert.False(written);
        Assert.Equal(first, File.ReadAllText(target));
    }

    [Fact]
    public void WriteIfChanged_DoesNotRewriteTheAvatar_WhenTheUrlIsUnchanged()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var payload = SamplePayload() with { Avatar = SampleAvatar("hash-a") };

        Assert.True(GuildRosterSync.WriteIfChanged(install, payload, stateStore));
        var written = File.GetLastWriteTimeUtc(StewardSyncFile.AvatarPathFor(install.AddOnsPath));

        Assert.False(GuildRosterSync.WriteIfChanged(install, payload, stateStore));
        Assert.Equal(written, File.GetLastWriteTimeUtc(StewardSyncFile.AvatarPathFor(install.AddOnsPath)));
    }

    [Fact]
    public void WriteIfChanged_RewritesTheAvatar_WhenTheUrlChanges()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload() with { Avatar = SampleAvatar("hash-a") }, stateStore));
        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload() with { Avatar = SampleAvatar("hash-b") }, stateStore));
    }

    [Fact]
    public void WriteIfChanged_RewritesTheAvatar_WhenTheAddonFolderWasReplaced()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var payload = SamplePayload() with { Avatar = SampleAvatar("hash-a") };

        Assert.True(GuildRosterSync.WriteIfChanged(install, payload, stateStore));
        File.Delete(StewardSyncFile.AvatarPathFor(install.AddOnsPath));

        Assert.True(GuildRosterSync.WriteIfChanged(install, payload, stateStore));
        Assert.True(File.Exists(StewardSyncFile.AvatarPathFor(install.AddOnsPath)));
    }

    [Fact]
    public void WriteIfChanged_Rewrites_WhenAnAddonUpdateRemovedTheSyncFile()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
        File.Delete(StewardSyncFile.PathFor(install.AddOnsPath));

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
        Assert.True(File.Exists(StewardSyncFile.PathFor(install.AddOnsPath)));
    }

    [Fact]
    public void WriteIfChanged_Rewrites_WhenAnAddonUpdateLeftThePlaceholder()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var target = StewardSyncFile.PathFor(install.AddOnsPath);

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
        File.WriteAllText(target, """Steward.LoadSync({ ["writtenAt"] = 0, ["members"] = {}, ["discord"] = {} })""");

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
        Assert.Equal(StewardSyncFile.Fingerprint(SamplePayload()), StewardSyncFile.ReadFingerprint(install.AddOnsPath));
    }

    [Fact]
    public void WriteIfChanged_Skips_WhenTheFileCarriesTheMatchingFingerprint()
    {
        var install = InstallAddon();
        StewardSyncFile.Write(install.AddOnsPath, SamplePayload());
        var first = File.ReadAllText(StewardSyncFile.PathFor(install.AddOnsPath));

        Assert.False(GuildRosterSync.WriteIfChanged(install, SamplePayload(), new AppStateStore(["steward"], StatePath)));
        Assert.Equal(first, File.ReadAllText(StewardSyncFile.PathFor(install.AddOnsPath)));
    }

    [Fact]
    public void WriteIfChanged_Rewrites_WhenTheFileCarriesAStaleFingerprint()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        StewardSyncFile.Write(install.AddOnsPath, SamplePayload() with { Discord = [] });

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
        Assert.Equal(StewardSyncFile.Fingerprint(SamplePayload()), StewardSyncFile.ReadFingerprint(install.AddOnsPath));
    }

    [Fact]
    public void WriteIfChanged_Rewrites_WhenTheFileIsUnreadable()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        File.WriteAllBytes(StewardSyncFile.PathFor(install.AddOnsPath), [0xFF, 0xFE, 0x00, 0x5B, 0x22]);

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
        Assert.Equal(StewardSyncFile.Fingerprint(SamplePayload()), StewardSyncFile.ReadFingerprint(install.AddOnsPath));
    }

    [Fact]
    public void ReadFingerprint_IsNull_WhenTheFileIsMissing()
    {
        var install = InstallAddon();

        Assert.Null(StewardSyncFile.ReadFingerprint(install.AddOnsPath));
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
