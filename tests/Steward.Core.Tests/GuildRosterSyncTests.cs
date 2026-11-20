namespace Steward.Core.Tests;

public sealed class GuildRosterSyncTests : IDisposable
{
    private const string SampleJson =
        """{"members":[{"userId":"111","name":"Hoobi","origin":["EU"],"flags":["core"],"notes":"Reliable","notesWarning":false}],"discord":[{"id":"222","name":"Grug"}],"statuses":["Officer","Raider"]}""";

    private readonly string _root = Directory.CreateTempSubdirectory("steward-roster-sync-").FullName;
    private string StatePath => Path.Combine(_root, "state.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SyncPayload Payload(string json, long writtenAt = 1758270000) =>
        SyncDocument.Parse($$"""{"version":1,"signOut":"dropMe","payload":{{json}}}""", DateTimeOffset.FromUnixTimeSeconds(writtenAt));

    private static SyncPayload SamplePayload() => Payload(SampleJson);

    private static AvatarImage SampleAvatar(string hash) =>
        new($"https://cdn.discordapp.com/avatars/1/{hash}.png?size=64", 64, 64, new byte[64 * 64 * 4]);

    private WowInstall InstallAddon(bool withToc = true, string product = ForeverLaunch.LiveProduct)
    {
        var addOnsPath = Path.Combine(_root, "AddOns");
        var addonPath = Path.Combine(addOnsPath, "Steward");
        Directory.CreateDirectory(addonPath);
        if (withToc)
        {
            File.WriteAllText(Path.Combine(addonPath, "Steward.toc"), "## Interface: 11507\n");
        }

        return new WowInstall(_root, "_forever_", _root, addOnsPath, product, null, "Forever");
    }

    [Fact]
    public void WriteIfChanged_WritesNothing_OnAClientOtherThanForever()
    {
        var install = InstallAddon(product: "wow_classic_era");
        var stateStore = new AppStateStore(["steward"], StatePath);

        Assert.False(GuildRosterSync.WriteIfChanged(install, SamplePayload() with { Avatar = SampleAvatar("hash-a") }, stateStore, force: true));
        Assert.False(File.Exists(StewardSyncFile.PathFor(install.AddOnsPath)));
        Assert.False(File.Exists(StewardSyncFile.AvatarPathFor(install.AddOnsPath)));
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
    public void WriteIfChanged_ReturnsFalseAndRecordsNothing_WhenTheFileCannotBeWritten()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file another handle opened with FileShare.None");
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var target = Path.Combine(install.AddOnsPath, "Steward", "StewardSync.lua");
        File.WriteAllText(target, "locked");

        bool written;
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            written = GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore);
        }

        Assert.False(written);
        Assert.False(stateStore.Load().GuildRosterSync.ContainsKey(install.FlavourPath));
    }

    [Fact]
    public void WriteIfChanged_ReturnsFalse_WhenTheTargetIsADirectory()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        Directory.CreateDirectory(Path.Combine(install.AddOnsPath, "Steward", "StewardSync.lua"));

        Assert.False(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
    }

    [Fact]
    public void WriteIfChanged_DoesNotRewrite_WhenTheDocumentIsUnchanged()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var target = Path.Combine(install.AddOnsPath, "Steward", "StewardSync.lua");

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));
        var first = File.ReadAllText(target);

        var written = GuildRosterSync.WriteIfChanged(install, Payload(SampleJson, writtenAt: 1758280000), stateStore);

        Assert.False(written);
        Assert.Equal(first, File.ReadAllText(target));
    }

    [Fact]
    public void WriteIfChanged_Rewrites_WhenForcedWithAnUnchangedDocument()
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
        StewardSyncFile.Write(install.AddOnsPath, Payload("""{"statuses":["Officer","Raider"]}"""));

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
    public void WriteIfChanged_Rewrites_WhenTheDocumentChanges()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);

        Assert.True(GuildRosterSync.WriteIfChanged(install, SamplePayload(), stateStore));

        var changed = Payload(SampleJson.Replace("\"name\":\"Grug\"", "\"name\":\"Grug\",\"nick\":\"grugnick\"", StringComparison.Ordinal));
        Assert.True(GuildRosterSync.WriteIfChanged(install, changed, stateStore));
    }

    private static string PersonUrl(string id) => $"https://cdn.discordapp.com/avatars/{id}/hash.png?size=64";

    private static AvatarImage PersonImage(string id) => new(PersonUrl(id), 64, 64, new byte[64 * 64 * 4]);

    private static SyncPayload PeoplePayload(IEnumerable<(string Id, bool HasUrl, bool HasImage)> people)
    {
        var list = people.ToList();
        var json = $$$"""{"directory":{"people":[{{{string.Join(',', list.Select(p => $$"""{"id":"{{p.Id}}","name":"Name{{p.Id}}"}"""))}}}],"characters":[]}}""";
        return Payload(json) with
        {
            Images = SyncImages.None with { People = list.Where(p => p.HasUrl).ToDictionary(p => p.Id, p => PersonUrl(p.Id)) },
            Avatars = list.Where(p => p.HasImage).ToDictionary(p => p.Id, p => PersonImage(p.Id)),
        };
    }

    private static LuaValue Rendered(WowInstall install)
    {
        var text = File.ReadAllText(Path.Combine(install.AddOnsPath, "Steward", "StewardSync.lua"));
        text = text.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        text = text[..text.LastIndexOf(')')];
        return LuaSavedVariables.Parse(text)["X"];
    }

    private static LuaValue RenderedPeople(WowInstall install) => Rendered(install).GetTable("directory")!.GetTable("people")!;

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
    public void WriteIfChanged_DeletesAvatarFiles_ForUsersNoLongerInTheImages()
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
    public void WriteIfChanged_DeletesTheAvatarFile_WhenThePersonLosesTheirImage()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("111", true, true)]), stateStore);

        GuildRosterSync.WriteIfChanged(install, PeoplePayload([("111", false, false)]), stateStore);

        Assert.False(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "111")));
        Assert.Null(Assert.Single(RenderedPeople(install).Items).GetString("avatar"));
    }

    [Fact]
    public void WriteIfChanged_WritesNoFile_ForAnImageIdThatIsNotDigits()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var payload = PeoplePayload([("111", true, true)]) with
        {
            Images = SyncImages.None with { People = new Dictionary<string, string> { ["111"] = PersonUrl("111"), [@"..\..\x"] = PersonUrl("1") } },
            Avatars = new Dictionary<string, AvatarImage> { ["111"] = PersonImage("111"), [@"..\..\x"] = PersonImage("1") },
        };

        GuildRosterSync.WriteIfChanged(install, payload, stateStore);

        Assert.Equal(["111.tga"], Directory.GetFiles(PersonAvatars.FolderFor(install.AddOnsPath)).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFiles(_root, "x*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LoadAsync_SkipsTheDownload_WhenTheUrlIsUnchangedAndTheFileExists()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var payload = PeoplePayload([("111", true, true)]);
        GuildRosterSync.WriteIfChanged(install, payload, stateStore);
        var downloads = 0;
        Task<AvatarImage?> Download(string url, int size, CancellationToken token)
        {
            downloads++;
            return Task.FromResult<AvatarImage?>(PersonImage("111"));
        }

        await PersonAvatars.LoadAsync(payload.Images, [install], stateStore, null, Download, CancellationToken.None);
        Assert.Equal(0, downloads);

        File.Delete(PersonAvatars.PathFor(install.AddOnsPath, "111"));
        var images = await PersonAvatars.LoadAsync(payload.Images, [install], stateStore, null, Download, CancellationToken.None);
        Assert.Equal(1, downloads);
        Assert.Contains("111", images.Keys);
    }

    private static string DiscordUrl(string id) => $"https://cdn.discordapp.com/avatars/{id}/hash.png?size=32";

    private static AvatarImage DiscordImage(string id) => new(DiscordUrl(id), 32, 32, new byte[32 * 32 * 4]);

    private static SyncPayload DiscordPayload(string discordJson = """[{"id":"111","name":"Hoobi"},{"id":"222","name":"Grug"},{"id":"333","name":"Plain"}]""") =>
        Payload($$$"""{"discord":{{{discordJson}}},"directory":{"people":[{"id":"111","name":"Hoobi"}],"characters":[]}}""") with
        {
            Images = new SyncImages(
                null,
                new Dictionary<string, string> { ["111"] = PersonUrl("111") },
                new Dictionary<string, string> { ["111"] = DiscordUrl("111"), ["222"] = DiscordUrl("222") }),
            Avatars = new Dictionary<string, AvatarImage> { ["111"] = PersonImage("111"), ["222"] = DiscordImage("222") },
        };

    [Fact]
    public void WriteIfChanged_WritesTheDiscordAvatarKeyAndFile_ForEachImageOnDisk()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);

        GuildRosterSync.WriteIfChanged(install, DiscordPayload(), stateStore);

        var rows = Rendered(install).GetTable("discord")!.Items.ToDictionary(row => row.GetString("id")!);
        Assert.Equal(@"Interface\AddOns\Steward\Avatars\222.tga", rows["222"].GetString("avatar"));
        Assert.Equal(@"Interface\AddOns\Steward\Avatars\111.tga", rows["111"].GetString("avatar"));
        Assert.Null(rows["333"].GetString("avatar"));
        Assert.Equal(18 + (32 * 32 * 4), new FileInfo(PersonAvatars.PathFor(install.AddOnsPath, "222")).Length);
        Assert.Equal(18 + (64 * 64 * 4), new FileInfo(PersonAvatars.PathFor(install.AddOnsPath, "111")).Length);
        Assert.Equal(2, Directory.GetFiles(PersonAvatars.FolderFor(install.AddOnsPath)).Length);
    }

    [Fact]
    public void WriteIfChanged_DoesNotRewriteADiscordFile_WhenItsUrlIsUnchanged()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        GuildRosterSync.WriteIfChanged(install, DiscordPayload(), stateStore);
        File.SetLastWriteTimeUtc(PersonAvatars.PathFor(install.AddOnsPath, "222"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var state = stateStore.Load();
        Assert.False(PersonAvatars.HasPendingWrite(install.AddOnsPath, DiscordPayload(), state.PersonAvatars[install.FlavourPath]));
        GuildRosterSync.WriteIfChanged(install, DiscordPayload("""[{"id":"111","name":"Hoobi"},{"id":"222","name":"Grug"},{"id":"444","name":"New"}]"""), stateStore);

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(PersonAvatars.PathFor(install.AddOnsPath, "222")));
    }

    [Fact]
    public void WriteIfChanged_DeletesTheDiscordFile_WhenTheMemberIsGone()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        GuildRosterSync.WriteIfChanged(install, DiscordPayload(), stateStore);

        GuildRosterSync.WriteIfChanged(install, DiscordPayload("[]") with
        {
            Images = new SyncImages(null, new Dictionary<string, string> { ["111"] = PersonUrl("111") }, new Dictionary<string, string>()),
            Avatars = new Dictionary<string, AvatarImage>(),
        }, stateStore);

        Assert.False(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "222")));
        Assert.True(File.Exists(PersonAvatars.PathFor(install.AddOnsPath, "111")));
    }

    [Fact]
    public async Task LoadAsync_RequestsThirtyTwoPixelsForDiscord_AndThePersonImageWinsASharedId()
    {
        var install = InstallAddon();
        var stateStore = new AppStateStore(["steward"], StatePath);
        var requests = new List<(string Url, int Size)>();
        Task<AvatarImage?> Download(string url, int size, CancellationToken token)
        {
            lock (requests)
            {
                requests.Add((url, size));
            }

            return Task.FromResult<AvatarImage?>(size == PersonAvatars.DiscordSize ? DiscordImage("222") : PersonImage("111"));
        }

        var images = await PersonAvatars.LoadAsync(DiscordPayload().Images, [install], stateStore, null, Download, CancellationToken.None);

        Assert.Equal([(DiscordUrl("222"), 32), (PersonUrl("111"), 64)], requests.OrderBy(r => r.Size));
        Assert.Equal(64, images["111"].Width);
        Assert.Equal(32, images["222"].Width);
        Assert.Equal(DiscordUrl("222"), PersonAvatars.RequestUri(new Uri(DiscordUrl("222")), PersonAvatars.DiscordSize));
    }
}
