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
    public void WriteIfChanged_Rewrites_WhenForcedWithAnUnchangedRoster()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore, force: true));
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

    private static string PersonUrl(string id) => $"https://cdn.discordapp.com/avatars/{id}/hash.png";

    private static AvatarImage PersonImage(string id) => new(PersonUrl(id) + "?size=64", 64, 64, new byte[64 * 64 * 4]);

    private static SyncPayload PeoplePayload(IEnumerable<(string Id, bool HasUrl, bool HasImage)> people) => SamplePayload() with
    {
        Directory = new SyncDirectory(
            [.. people.Select(p => new DirectoryPerson(p.Id, "Name" + p.Id, null, p.HasUrl ? PersonUrl(p.Id) : null))],
            [],
            null)
        {
            Avatars = people.Where(p => p.HasImage).ToDictionary(p => p.Id, p => PersonImage(p.Id)),
        },
    };

    private static LuaValue RenderedPeople(WowInstall install)
    {
        var text = File.ReadAllText(Path.Combine(install.AddOnsPath, "Steward", "StewardSync.lua"));
        text = text.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        text = text[..text.LastIndexOf(')')];
        return LuaSavedVariables.Parse(text)["X"].GetTable("directory")!.GetTable("people")!;
    }

    [Fact]
    public void WriteIfChanged_SetsTheAvatarKeyOnlyForPeopleWhoseFileExists()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);

        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("111", true, true), ("222", true, false), ("333", false, false)]), stateStore);

        var people = RenderedPeople(install).Items.ToDictionary(p => p.GetString("id")!);
        Assert.Equal(@"Interface\AddOns\Steward\Avatars\111.tga", people["111"].GetString("avatar"));
        Assert.Null(people["222"].GetString("avatar"));
        Assert.Null(people["333"].GetString("avatar"));
        Assert.True(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "111")));
        Assert.False(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "222")));
    }

    [Fact]
    public void WriteIfChanged_KeepsTheAvatarKey_WhenTheFileIsAlreadyOnDisk()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("111", true, true)]), stateStore);

        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("111", true, false), ("222", true, false)]), stateStore);

        var people = RenderedPeople(install).Items.ToDictionary(p => p.GetString("id")!);
        Assert.NotNull(people["111"].GetString("avatar"));
        Assert.Null(people["222"].GetString("avatar"));
    }

    [Fact]
    public void WriteIfChanged_DeletesAvatarFiles_ForUsersNoLongerInTheDirectory()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("111", true, true), ("222", true, true)]), stateStore);
        File.WriteAllBytes(PersonAvatars.PathFor(install.AddOnsPath, "999"), [1]);

        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("222", true, false), ("333", false, false)]), stateStore);

        Assert.False(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "111")));
        Assert.False(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "999")));
        Assert.True(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "222")));
    }

    [Fact]
    public void WriteIfChanged_DeletesTheAvatarFile_WhenThePersonLosesTheirAvatarUrl()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("111", true, true)]), stateStore);

        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("111", false, false)]), stateStore);

        Assert.False(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "111")));
        Assert.Null(Assert.Single(RenderedPeople(install).Items).GetString("avatar"));
    }

    [Fact]
    public async Task LoadAsync_SkipsTheDownload_WhenTheUrlIsUnchangedAndTheFileExists()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var payload = PeoplePayload([("111", true, true)]);
        GuildRosterSync.WriteIfChanged(install, payload, stateStore);
        var downloads = 0;
        Task<AvatarImage?> Download(string url, CancellationToken token)
        {
            downloads++;
            return Task.FromResult<AvatarImage?>(PersonImage("111"));
        }

        await PersonAvatars.LoadAsync(payload.Directory!.People!, [install], stateStore, null, Download, CancellationToken.None);
        Assert.Equal(0, downloads);

        File.Delete(PersonAvatars.PathFor(install.AddOnsPath, "111"));
        var images = await PersonAvatars.LoadAsync(payload.Directory.People!, [install], stateStore, null, Download, CancellationToken.None);
        Assert.Equal(1, downloads);
        Assert.Contains("111", images.Keys);
    }
}
