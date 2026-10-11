using System.Net;
using System.Text;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class AccessLossTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("steward-access-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Flavour => Path.Combine(_root, "_classic_beta_");

    private static ManagedAddon Addon(string id, string source = "Steward", bool? curseForge = null) =>
        new(id, id == "hoobiscripts" ? "HoobiScripts" : id, $"https://example.com/{id}/", Name: id == "hoobiscripts" ? "HoobiScripts" : null, Source: source) { CurseForge = curseForge };

    private static CatalogueAddon Row(string id) => new(id, id, $"https://example.com/{id}/");

    private AppState StateWith(params string[] installedIds) =>
        new([], installedIds.ToDictionary(id => AppStateStore.Key(Flavour, id), _ => new InstalledAddonRecord("1.0.0", "release", "abc"), StringComparer.OrdinalIgnoreCase))
        {
            AddedInstalls = [],
            IgnoredAddons = [],
            LostAccess = new(StringComparer.OrdinalIgnoreCase),
        };

    private sealed class StubHandler(HttpStatusCode status, string body, string mediaType = "application/json") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
    }

    private static HttpClient ClientFor(HttpStatusCode status, string body, Action onRevoked, string mediaType = "application/json") =>
        new(new SessionRevokedHandler(onRevoked) { InnerHandler = new StubHandler(status, body, mediaType) });

    [Fact]
    public async Task Handler_RevokedUnauthorized_ReportsAndKeepsTheBodyReadable()
    {
        var revoked = 0;
        using var client = ClientFor(HttpStatusCode.Unauthorized, """{"error":"revoked"}""", () => revoked++);

        using var response = await client.GetAsync("https://api.example.com/guild/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(1, revoked);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"error":"revoked"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"expired"}""")]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.Unauthorized, "<html><body>Cloudflare</body></html>")]
    [InlineData(HttpStatusCode.Unauthorized, """["revoked"]""")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":"revoked"}""")]
    [InlineData(HttpStatusCode.BadGateway, """{"error":"revoked"}""")]
    [InlineData(HttpStatusCode.OK, """{"error":"revoked"}""")]
    public async Task Handler_AnythingButARevokedUnauthorized_DoesNotReport(HttpStatusCode status, string body)
    {
        var revoked = 0;
        using var client = ClientFor(status, body, () => revoked++);

        using var response = await client.GetAsync("https://api.example.com/guild/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(0, revoked);
    }

    [Fact]
    public async Task GetMe_RevokedUnauthorized_ThrowsSessionExpiredAndReports()
    {
        var revoked = 0;
        var client = new StewardClient(ClientFor(HttpStatusCode.Unauthorized, """{"error":"revoked"}""", () => revoked++), "https://api.example.com/guild", "Steward/1.0.0 (dev)");

        await Assert.ThrowsAsync<SessionExpiredException>(() => client.GetMeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, revoked);
    }

    [Fact]
    public async Task GetMe_PlainUnauthorized_ThrowsSessionExpiredWithoutReporting()
    {
        var revoked = 0;
        var client = new StewardClient(ClientFor(HttpStatusCode.Unauthorized, "", () => revoked++), "https://api.example.com/guild", "Steward/1.0.0 (dev)");

        await Assert.ThrowsAsync<SessionExpiredException>(() => client.GetMeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, revoked);
    }

    [Fact]
    public async Task GetMe_ServerError_ThrowsHttpRequestWithoutReporting()
    {
        var revoked = 0;
        var client = new StewardClient(ClientFor(HttpStatusCode.ServiceUnavailable, """{"error":"revoked"}""", () => revoked++), "https://api.example.com/guild", "Steward/1.0.0 (dev)");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetMeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, revoked);
    }

    [Fact]
    public async Task GetMe_HtmlOk_ThrowsRatherThanReturningAnAnswer()
    {
        var client = new StewardClient(ClientFor(HttpStatusCode.OK, "<html>Just a moment...</html>", () => { }, "text/html"), "https://api.example.com/guild", "Steward/1.0.0 (dev)");

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetMeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Revoke_QueuesEveryInstalledStewardAndProtectedRowAtOnce()
    {
        var state = StateWith("hoobiscripts", "steward", "restedxp", "curseforge-6273-88568");
        IReadOnlyList<ManagedAddon> catalogue =
        [
            Addon("hoobiscripts", "Protected"),
            Addon("steward"),
            Addon("restedxp", "GitHub"),
            Addon("curseforge-6273-88568", "CurseForge"),
            Addon("bugsack", "Steward", curseForge: true),
            Addon("stewardguides"),
        ];

        var lost = AccessLoss.Revoke(state, catalogue, Now);

        Assert.Equal(["hoobiscripts", "steward"], lost.Values.Select(entry => entry.Id).Order());
        Assert.All(lost.Values, entry => Assert.True(entry.Due));
        Assert.Equal(Flavour, lost[AppStateStore.Key(Flavour, "hoobiscripts")].FlavourPath);
        Assert.Equal("HoobiScripts", lost[AppStateStore.Key(Flavour, "hoobiscripts")].Name);
    }

    [Fact]
    public void Revoke_UsesTheRecordedFoldersAndTheAddonFolder()
    {
        var state = StateWith("hoobiscripts");
        state.Installs[AppStateStore.Key(Flavour, "hoobiscripts")] = new InstalledAddonRecord("1.0.0", "release", "abc", Folders: ["HoobiScripts", "HoobiScripts_Extra"]);

        var lost = AccessLoss.Revoke(state, [Addon("hoobiscripts", "Protected")], Now);

        Assert.Equal(["HoobiScripts", "HoobiScripts_Extra"], lost.Values.Single().Folders);
    }

    [Fact]
    public void Track_FirstCheckWithoutTheRow_RecordsItButIsNotDue()
    {
        var lost = AccessLoss.Track(StateWith("hoobiscripts"), [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now);

        var entry = Assert.Single(lost.Values);
        Assert.Equal("hoobiscripts", entry.Id);
        Assert.Equal(Now, entry.FirstMissing);
        Assert.False(entry.Due);
    }

    [Fact]
    public void Track_SecondCheckUnderThirtyMinutesLater_IsNotDue()
    {
        var state = StateWith("hoobiscripts");
        var first = AccessLoss.Track(state, [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now);

        var second = AccessLoss.Track(state with { LostAccess = first }, [Addon("steward")], [Row("steward")], Now.AddMinutes(29));

        Assert.False(Assert.Single(second.Values).Due);
    }

    [Fact]
    public void Track_SecondCheckThirtyMinutesLater_IsDue()
    {
        var state = StateWith("hoobiscripts");
        var first = AccessLoss.Track(state, [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now);

        var second = AccessLoss.Track(state with { LostAccess = first }, [Addon("steward")], [Row("steward")], Now.AddMinutes(30));

        var entry = Assert.Single(second.Values);
        Assert.True(entry.Due);
        Assert.Equal(Now, entry.FirstMissing);
    }

    [Fact]
    public void Track_RowReappears_ClearsTheRecord()
    {
        var state = StateWith("hoobiscripts");
        var first = AccessLoss.Track(state, [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now);

        var back = AccessLoss.Track(state with { LostAccess = first }, [Addon("steward")], [Row("steward"), Row("hoobiscripts")], Now.AddHours(1));

        Assert.Empty(back);
    }

    [Fact]
    public void Track_RowReappears_ClearsARevokedRecordToo()
    {
        var state = StateWith("hoobiscripts");
        var revoked = AccessLoss.Revoke(state, [Addon("hoobiscripts", "Protected")], Now);

        var back = AccessLoss.Track(state with { LostAccess = revoked }, null, [Row("hoobiscripts")], Now.AddMinutes(1));

        Assert.Empty(back);
    }

    [Fact]
    public void Track_InstallRecordGone_ClearsTheRecord()
    {
        var state = StateWith("hoobiscripts");
        var first = AccessLoss.Track(state, [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now);

        var later = AccessLoss.Track(StateWith() with { LostAccess = first }, [Addon("steward")], [Row("steward")], Now.AddHours(1));

        Assert.Empty(later);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Track_EmptyOrMissingAddonList_ChangesNothing(bool empty)
    {
        var state = StateWith("hoobiscripts");
        var first = AccessLoss.Track(state, [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now);

        var later = AccessLoss.Track(state with { LostAccess = first }, [Addon("hoobiscripts", "Protected")], empty ? [] : null, Now.AddHours(2));

        Assert.False(Assert.Single(later.Values).Due);
        Assert.Empty(AccessLoss.Track(state, [Addon("hoobiscripts", "Protected")], empty ? [] : null, Now));
    }

    [Theory]
    [InlineData("GitHub", null)]
    [InlineData("CurseForge", null)]
    [InlineData("Steward", true)]
    public void Track_RowsNotFromStewardOrProtected_AreNeverTracked(string source, bool? curseForge)
    {
        var lost = AccessLoss.Track(StateWith("other"), [Addon("other", source, curseForge), Addon("steward")], [Row("steward")], Now);

        Assert.Empty(lost);
    }

    [Fact]
    public void Track_RowNotInstalledByTheCompanion_IsNeverTracked()
    {
        var lost = AccessLoss.Track(StateWith(), [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now);

        Assert.Empty(lost);
    }

    [Fact]
    public void FirstMissingTime_SurvivesASaveAndLoad()
    {
        var store = new AppStateStore([], Path.Combine(_root, "state.json"));
        var state = StateWith("hoobiscripts");
        store.Save(state with { LostAccess = AccessLoss.Track(state, [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now) });

        var loaded = new AppStateStore([], Path.Combine(_root, "state.json")).Load();
        var due = AccessLoss.Track(loaded, [Addon("steward")], [Row("steward")], Now.AddMinutes(31));

        Assert.Equal(Now, loaded.LostAccess.Values.Single().FirstMissing);
        Assert.True(due.Values.Single().Due);
    }

    [Fact]
    public void RemoveInstall_DropsLostAccessRecordsForThatPath()
    {
        var state = StateWith("hoobiscripts");
        state = state with { LostAccess = AccessLoss.Revoke(state, [Addon("hoobiscripts", "Protected")], Now) };

        Assert.Empty(AppStateStore.RemoveInstall(state, Flavour).LostAccess);
    }

    private string MakeAddon(string folder, bool withToc = true)
    {
        var path = Path.Combine(AccessLoss.AddOnsPath(Flavour), folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, withToc ? $"{folder}.toc" : "readme.txt"), "## Title: x");
        return path;
    }

    [Fact]
    public void RemoveDue_DeletesTheFoldersAndForgetsTheInstall()
    {
        var folder = MakeAddon("HoobiScripts");
        var wtf = Path.Combine(Flavour, "WTF", "Account", "A", "SavedVariables");
        Directory.CreateDirectory(wtf);
        File.WriteAllText(Path.Combine(wtf, "HoobiScripts.lua"), "HoobiScriptsDB = {}");
        var key = AppStateStore.Key(Flavour, "hoobiscripts");
        var state = StateWith("hoobiscripts", "steward");
        state.IgnoredAddons.Add(key);
        state = state with { LostAccess = AccessLoss.Revoke(state, [Addon("hoobiscripts", "Protected")], Now) };

        var removed = AccessLoss.RemoveDue(state, _ => false);

        Assert.Equal("hoobiscripts", Assert.Single(removed).Id);
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(wtf, "HoobiScripts.lua")));
        Assert.False(state.Installs.ContainsKey(key));
        Assert.True(state.Installs.ContainsKey(AppStateStore.Key(Flavour, "steward")));
        Assert.Empty(state.IgnoredAddons);
        Assert.Empty(state.LostAccess);
        Assert.Equal("HoobiScripts was removed because your access ended", AccessLoss.Notice(removed[0]));
    }

    [Fact]
    public void RemoveDue_WhileWowRuns_KeepsEverythingForTheNextPass()
    {
        var folder = MakeAddon("HoobiScripts");
        var state = StateWith("hoobiscripts");
        state = state with { LostAccess = AccessLoss.Revoke(state, [Addon("hoobiscripts", "Protected")], Now) };

        var removed = AccessLoss.RemoveDue(state, entry => entry.FlavourPath == Flavour);

        Assert.Empty(removed);
        Assert.True(Directory.Exists(folder));
        Assert.Single(state.LostAccess);
        Assert.True(state.Installs.ContainsKey(AppStateStore.Key(Flavour, "hoobiscripts")));
    }

    [Fact]
    public void RemoveDue_LeavesRecordsThatAreNotDue()
    {
        var folder = MakeAddon("HoobiScripts");
        var state = StateWith("hoobiscripts");
        state = state with { LostAccess = AccessLoss.Track(state, [Addon("hoobiscripts", "Protected"), Addon("steward")], [Row("steward")], Now) };

        Assert.Empty(AccessLoss.RemoveDue(state, _ => false));
        Assert.True(Directory.Exists(folder));
        Assert.Single(state.LostAccess);
    }

    [Fact]
    public void RemoveDue_FolderWithoutItsOwnToc_IsRefusedAndForgotten()
    {
        var folder = MakeAddon("HoobiScripts", withToc: false);
        var state = StateWith("hoobiscripts");
        state = state with { LostAccess = AccessLoss.Revoke(state, [Addon("hoobiscripts", "Protected")], Now) };
        Exception? failure = null;

        var removed = AccessLoss.RemoveDue(state, _ => false, (_, ex) => failure = ex);

        Assert.Empty(removed);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.True(Directory.Exists(folder));
        Assert.Empty(state.LostAccess);
    }

    [Fact]
    public void LostAccess_SerialisesUnderItsOwnKey()
    {
        var state = StateWith("hoobiscripts");
        state = state with { LostAccess = AccessLoss.Revoke(state, [Addon("hoobiscripts", "Protected")], Now) };

        var json = JsonSerializer.Serialize(state, CompanionJsonContext.Default.AppState);

        Assert.Contains("\"lost_access\"", json, StringComparison.Ordinal);
        Assert.Contains("\"first_missing\"", json, StringComparison.Ordinal);
    }
}
