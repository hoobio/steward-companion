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
            """[{"folder":"Questie","modId":334372,"fileId":null,"folders":["QuestieDB","Questie"]}]""");

        var matches = await client.MatchCurseForgeAsync(88568,
            new CurseForgeMatchRequest([new CurseForgeDeclared("Questie", 334372)], [new CurseForgeFolderFingerprint("QuestieDB", 4000000000)]),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Contains("\"fingerprint\":4000000000", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"modId\":334372", handler.RequestBody, StringComparison.Ordinal);
        var match = Assert.Single(matches);
        Assert.Null(match.FileId);
        Assert.Equal(["QuestieDB", "Questie"], match.Folders);
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
}
