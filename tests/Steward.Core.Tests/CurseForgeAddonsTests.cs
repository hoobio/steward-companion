using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class CurseForgeAddonsTests
{
    private static readonly HashSet<string> OnDisk = new(["Questie", "QuestieDB", "DBM-Core", "DBM-GUI", "Details"], StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<ProviderAddonRecord> Adopt(params CurseForgeMatch[] matches) =>
        CurseForgeAddons.Adopt(matches, 88568, OnDisk.Contains, folder => folder == "Questie" ? "Questie Classic" : null);

    [Fact]
    public void Adopt_FoldsEveryModuleOfAModIntoOneRecord_NamedByThePrefixFolder()
    {
        var adopted = Assert.Single(Adopt(
            new CurseForgeMatch("QuestieDB", 334372, 7, ["QuestieDB", "Questie"]),
            new CurseForgeMatch("Questie", 334372, 7, ["QuestieDB", "Questie"])));

        Assert.Equal("curseforge-334372-88568", adopted.Id);
        Assert.Equal("Questie", adopted.FolderName);
        Assert.Equal("Questie Classic", adopted.Name);
        Assert.Equal(CurseForgeAddons.Source, adopted.Source);
        Assert.Equal(["QuestieDB", "Questie"], adopted.Folders);
    }

    [Fact]
    public void Adopt_PrefersTheMatchesOwnName_OverTheTocTitle()
    {
        var adopted = Assert.Single(Adopt(
            new CurseForgeMatch("Questie", 334372, 7, ["Questie"], "Questie", "https://www.curseforge.com/wow/addons/questie")));

        Assert.Equal("Questie", adopted.Name);
        Assert.Equal("https://www.curseforge.com/wow/addons/questie", adopted.WebsiteUrl);
    }

    [Fact]
    public void Adopt_WithNoNameOnTheMatch_FallsBackToTheTocTitle()
    {
        var adopted = Assert.Single(Adopt(new CurseForgeMatch("Questie", 334372, 7, ["Questie"])));

        Assert.Equal("Questie Classic", adopted.Name);
        Assert.Null(adopted.WebsiteUrl);
    }

    [Fact]
    public void Adopt_DeclaredIdWinsOverAFingerprintForTheSameFolder()
    {
        var adopted = Assert.Single(Adopt(
            new CurseForgeMatch("Details", 999, null, ["Details"]),
            new CurseForgeMatch("Details", 111, 5, ["Details"])));

        Assert.Equal(999, adopted.ModId);
    }

    [Fact]
    public void Adopt_SkipsAModWithNoFolderOnDisk_AndFallsBackToTheFolderName()
    {
        var adopted = Adopt(
            new CurseForgeMatch("Gone", 1, 3, ["Gone"]),
            new CurseForgeMatch("DBM-GUI", 2, 4, ["DBM-Core", "DBM-GUI", "DBM-Missing"]));

        var dbm = Assert.Single(adopted);
        Assert.Equal("DBM-Core", dbm.FolderName);
        Assert.Equal("DBM-Core", dbm.Name);
    }

    [Fact]
    public void Adopt_ASingleMatch_GivesOneRecordHoldingEveryModuleOnDisk()
    {
        var adopted = Assert.Single(Adopt(new CurseForgeMatch("DBM-Core", 2, 4, ["DBM-Core", "DBM-GUI"], "Deadly Boss Mods", "https://www.curseforge.com/wow/addons/dbm")));

        Assert.Equal(CurseForgeAddons.Id(2, 88568), adopted.Id);
        Assert.Equal("DBM-Core", adopted.FolderName);
        Assert.Equal("Deadly Boss Mods", adopted.Name);
        Assert.Equal(["DBM-Core", "DBM-GUI"], adopted.Folders);
        Assert.Equal("https://www.curseforge.com/wow/addons/dbm", adopted.WebsiteUrl);
    }

    [Fact]
    public void Adoptable_DropsRecordedAndKeptLocalMatches()
    {
        const string Flavour = @"C:\wow\_classic_beta_";
        var questie = new ProviderAddonRecord(CurseForgeAddons.Id(1, 88568), "Questie", "Questie", "CurseForge", 1, 88568, ["Questie"]);
        var dbm = new ProviderAddonRecord(CurseForgeAddons.Id(2, 88568), "DBM-Core", "DBM", "CurseForge", 2, 88568, ["DBM-Core"]);
        var details = new ProviderAddonRecord(CurseForgeAddons.Id(3, 88568), "Details", "Details", "CurseForge", 3, 88568, ["Details"]);

        var adoptable = CurseForgeAddons.Adoptable([questie, dbm, details], [dbm], [AppStateStore.Key(Flavour, "DETAILS")], Flavour);

        Assert.Equal([questie], adoptable);
    }

    [Fact]
    public void KeptLocalKey_IsTheInstallAndPrimaryFolder() =>
        Assert.Equal(
            AppStateStore.Key(@"C:\wow", "Questie"),
            CurseForgeAddons.KeptLocalKey(@"C:\wow", new ProviderAddonRecord("id", "Questie", "Q", "CurseForge", 1, 2, ["Questie", "QuestieDB"])));

    [Fact]
    public void MatchFor_FindsTheRecordHoldingAnyOfTheRowsFolders()
    {
        var questie = new ProviderAddonRecord("q", "Questie", "Questie", "CurseForge", 1, 2, ["Questie", "QuestieDB"]);
        var dbm = new ProviderAddonRecord("d", "DBM-Core", "DBM", "CurseForge", 2, 2, ["DBM-Core"]);

        Assert.Same(questie, CurseForgeAddons.MatchFor([dbm, questie], new LocalAddon("QuestieDB", "QuestieDB", null, null, [])));
        Assert.Same(dbm, CurseForgeAddons.MatchFor([dbm, questie], new LocalAddon("DBM-GUI", "DBM", null, null, ["dbm-core"])));
        Assert.Null(CurseForgeAddons.MatchFor([dbm, questie], new LocalAddon("Details", "Details", null, null, [])));
    }

    [Fact]
    public void Folders_PutsTheFolderNameFirstWithoutDuplicates() =>
        Assert.Equal(["Questie", "QuestieDB"], CurseForgeAddons.Folders(new ProviderAddonRecord("id", "Questie", "Q", "CurseForge", 1, 2, ["QuestieDB", "Questie"])));

    [Fact]
    public void ToManagedAddon_IsGatedOnCurseForge_AndUsesTheStoredIcon()
    {
        var addon = CurseForgeAddons.ToManagedAddon(
            new ProviderAddonRecord("curseforge-1-2", "Questie", "Questie", "CurseForge", 1, 2, ["Questie"], "https://media.forgecdn.net/x.png"),
            "https://api.example.com/guild/api/addons/curseforge/1/2/");

        Assert.Equal([GigagrugClient.CurseForgeFeature], addon.Features);
        Assert.Equal(new Uri("https://media.forgecdn.net/x.png"), addon.IconUri);
        Assert.Equal("CurseForge", addon.Source);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request };
        }
    }

    private static (GigagrugClient Client, StubHandler Handler) ClientFor(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new GigagrugClient(new HttpClient(handler), "https://api.example.com/guild", "Steward/1.0.0 (dev)"), handler);
    }

    [Fact]
    public async Task ReportCurseForgeDownloadFailureAsync_PostsTheBody_AndThrowsOnNotFound()
    {
        var (client, handler) = ClientFor(HttpStatusCode.NoContent, "");
        await client.ReportCurseForgeDownloadFailureAsync(new CurseForgeDownloadFailure(334372, "v2", "https://edge.example/q.zip", null, "checksum mismatch"), CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/addons/curseforge/download-failure", handler.Request?.RequestUri?.ToString());
        Assert.Equal("""{"modId":334372,"version":"v2","url":"https://edge.example/q.zip","status":null,"error":"checksum mismatch"}""", handler.RequestBody);

        var (missing, _) = ClientFor(HttpStatusCode.NotFound, "");
        await Assert.ThrowsAsync<HttpRequestException>(() => missing.ReportCurseForgeDownloadFailureAsync(new CurseForgeDownloadFailure(1, "v", "u", 403, "e"), CancellationToken.None));
    }

    [Fact]
    public async Task SearchCurseForgeAsync_SearchUnavailable_ReturnsNull()
    {
        var (client, _) = ClientFor(HttpStatusCode.ServiceUnavailable, """{"error":"search_unavailable"}""");

        Assert.Null(await client.SearchCurseForgeAsync(88568, "questie", CancellationToken.None));
    }

    [Fact]
    public async Task SearchCurseForgeAsync_CurseForgeUnavailable_Throws()
    {
        var (client, _) = ClientFor(HttpStatusCode.ServiceUnavailable, """{"error":"curseforge_unavailable"}""");

        await Assert.ThrowsAsync<GigagrugRequestException>(() => client.SearchCurseForgeAsync(88568, "questie", CancellationToken.None));
    }

    [Fact]
    public async Task SearchCurseForgeAsync_EscapesTheQuery_AndParsesResults()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK,
            """{"results":[{"id":334372,"name":"Questie","summary":"Quests","author":"Aero","iconUrl":null,"websiteUrl":"https://www.curseforge.com/wow/addons/questie","downloadCount":1.5E7,"latestVersion":"11.0","allowDistribution":true}]}""");

        var result = Assert.Single((await client.SearchCurseForgeAsync(88568, "a b&c", CancellationToken.None))!);

        Assert.Equal("https://api.example.com/guild/api/addons/curseforge/search?versionType=88568&q=a%20b%26c", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal(334372, result.Id);
        Assert.True(result.AllowDistribution);
    }

    [Fact]
    public async Task GetCurseForgeDiscoverAsync_ListsPopularThenRecentlyUpdated_WithoutDuplicates()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK,
            """{"popular":[{"id":1,"name":"A"},{"id":2,"name":"B"}],"recentlyUpdated":[{"id":2,"name":"B"},{"id":3,"name":"C","allowDistribution":false}]}""");

        var results = await client.GetCurseForgeDiscoverAsync(88568, CancellationToken.None);

        Assert.Equal([1, 2, 3], results.Select(result => result.Id));
        Assert.False(results[2].AllowDistribution);
    }

    [Fact]
    public async Task MatchCurseForgeAsync_PostsDeclaredAndFingerprints_AndParsesTheBareArray()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK,
            """[{"folder":"Questie","modId":334372,"fileId":null,"folders":["QuestieDB","Questie"],"name":"Questie","websiteUrl":"https://www.curseforge.com/wow/addons/questie"}]""");

        var matches = await client.MatchCurseForgeAsync(88568,
            new CurseForgeMatchRequest([new CurseForgeDeclared("Questie", 334372)], [new CurseForgeFolderFingerprint("QuestieDB", 4000000000)]),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Contains("\"fingerprint\":4000000000", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"modId\":334372", handler.RequestBody, StringComparison.Ordinal);
        var match = Assert.Single(matches);
        Assert.Null(match.FileId);
        Assert.Equal(["QuestieDB", "Questie"], match.Folders);
        Assert.Equal("Questie", match.Name);
        Assert.Equal("https://www.curseforge.com/wow/addons/questie", match.WebsiteUrl);
    }

    [Fact]
    public async Task GetCurseForgeFileAsync_ParsesTheModAndTheFileManifest()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK,
            """{"modId":3358,"name":"Deadly Boss Mods","iconUrl":"https://media.forgecdn.net/x.png","websiteUrl":"https://www.curseforge.com/wow/addons/deadly-boss-mods","allowDistribution":false,"file":{"version":"11.0.1","size":5,"released":"2026-09-20T00:00:00Z","folders":["DBM-Core"],"distributable":false},"releaseType":2,"gameVersionTypeIds":[517,88568]}""");

        var file = await client.GetCurseForgeFileAsync(3358, 8996374, CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/addons/curseforge/3358/files/8996374", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.NotNull(file);
        Assert.Equal("Deadly Boss Mods", file.Name);
        Assert.False(file.AllowDistribution);
        Assert.False(file.File.Distributable);
        Assert.Null(file.File.Zip);
        Assert.Equal(2, file.ReleaseType);
        Assert.Equal([517, 88568], file.GameVersionTypeIds);
    }

    [Fact]
    public async Task GetCurseForgeFileAsync_NotFound_ReturnsNull()
    {
        var (client, _) = ClientFor(HttpStatusCode.NotFound, """{"error":"not found"}""");

        Assert.Null(await client.GetCurseForgeFileAsync(1, 2, CancellationToken.None));
    }

    [Fact]
    public async Task MatchCurseForgeAsync_Unauthorized_ThrowsSessionExpired()
    {
        var (client, _) = ClientFor(HttpStatusCode.Unauthorized, "");

        await Assert.ThrowsAsync<SessionExpiredException>(() =>
            client.MatchCurseForgeAsync(88568, new CurseForgeMatchRequest([], []), CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestAsync_CurseForgeAddon_UsesTheSessionClient()
    {
        var plain = new StubHandler(HttpStatusCode.OK, "null");
        var session = new StubHandler(HttpStatusCode.OK, "null");
        var updater = new AddonUpdater(new HttpClient(plain), sessionClient: new HttpClient(session));
        var addon = CurseForgeAddons.ToManagedAddon(
            new ProviderAddonRecord("curseforge-1-2", "Questie", "Questie", "CurseForge", 1, 2, ["Questie"]),
            "https://api.example.com/guild/api/addons/curseforge/1/2/");

        await updater.GetLatestAsync(addon, "release", CancellationToken.None);

        Assert.Null(plain.Request);
        Assert.Equal("https://api.example.com/guild/api/addons/curseforge/1/2/latest-release.json", session.Request!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetLatestAsync_Unauthorized_ThrowsSessionExpired()
    {
        var updater = new AddonUpdater(new HttpClient(new StubHandler(HttpStatusCode.OK, "null")), sessionClient: new HttpClient(new StubHandler(HttpStatusCode.Unauthorized, "")));
        var addon = CurseForgeAddons.ToManagedAddon(
            new ProviderAddonRecord("curseforge-1-2", "Questie", "Questie", "CurseForge", 1, 2, ["Questie"]),
            "https://api.example.com/guild/api/addons/curseforge/1/2/");

        await Assert.ThrowsAsync<SessionExpiredException>(() => updater.GetLatestAsync(addon, "release", CancellationToken.None));
    }

    private static readonly string[] AtlasLoot =
    [
        "AtlasBIStooltips", "AtlasLootClassic", "AtlasLootClassic_Collections", "AtlasLootClassic_Crafting", "AtlasLootClassic_Data",
        "AtlasLootClassic_DungeonsAndRaids", "AtlasLootClassic_Factions", "AtlasLootClassic_Options", "AtlasLootClassic_PvP",
    ];

    [Fact]
    public void PrimaryFolder_IsTheFolderThatPrefixesTheMostOthers() =>
        Assert.Equal("AtlasLootClassic", CurseForgeAddons.PrimaryFolder(AtlasLoot));

    [Fact]
    public void PrimaryFolder_PrefersQuestieOverQuestieDb() =>
        Assert.Equal("Questie", CurseForgeAddons.PrimaryFolder(["QuestieDB", "Questie"]));

    [Fact]
    public void PrimaryFolder_KeepsTheFirstWhenNoFolderPrefixesAnother() =>
        Assert.Equal("Bagnon", CurseForgeAddons.PrimaryFolder(["Bagnon", "Wildpants", "BagBrother"]));

    [Fact]
    public void WithPrimaryFolder_MigratesARecordNamedAfterItsFirstFolder()
    {
        var record = new ProviderAddonRecord("curseforge-1422985-88568", "AtlasBIStooltips", "AtlasLoot", CurseForgeAddons.Source, 1422985, 88568, AtlasLoot);

        Assert.Equal("AtlasLootClassic", CurseForgeAddons.WithPrimaryFolder(record, _ => true).FolderName);
    }

    [Fact]
    public void WithPrimaryFolder_NeverMovesToAFolderThatIsNotOnDisk()
    {
        var record = new ProviderAddonRecord("curseforge-1-88568", "QuestieDB", "Questie", CurseForgeAddons.Source, 1, 88568, ["Questie", "QuestieDB"]);

        Assert.Same(record, CurseForgeAddons.WithPrimaryFolder(record, _ => false));
    }
}
