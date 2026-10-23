using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class CurseForgeDirectTests : IDisposable
{
    private const int VersionType = 88568;
    private const string ProxyBase = "https://api.example.com/guild/api/addons/curseforge/334372/88568/";
    private const string ProxyManifest = """{"version":"proxy-2","zip":"https://edge.forgecdn.net/files/9/9/Proxy.zip","size":%SIZE%,"released":"2026-10-12T00:00:00Z","folders":["Questie"]}""";

    private static readonly ManagedAddon Questie = CurseForgeAddons.ToManagedAddon(
        new ProviderAddonRecord("curseforge-334372-88568", "Questie", "Questie", CurseForgeAddons.Source, 334372, VersionType, ["Questie"], WebsiteUrl: "https://www.curseforge.com/wow/addons/questie"),
        ProxyBase);

    private readonly string _tempDir = Directory.CreateTempSubdirectory("steward-cfdirect-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static string Item(long id, string? displayName, string fileName, string created, int releaseType = 1, int versionType = VersionType, int status = 4, bool earlyAccess = false) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["displayName"] = displayName,
            ["fileName"] = fileName,
            ["fileLength"] = 123,
            ["dateCreated"] = created,
            ["releaseType"] = releaseType,
            ["gameVersionTypeIds"] = new[] { 517, versionType },
            ["status"] = status,
            ["isEarlyAccessContent"] = earlyAccess,
        });

    private static string Body(params string[] items) => $$$"""{"data":[{{{string.Join(",", items)}}}],"pagination":{"index":0,"pageSize":50,"totalCount":{{{items.Length}}}}}""";

    private static IReadOnlyList<CurseForgePublicFile> Files(params string[] items)
    {
        using var document = JsonDocument.Parse(Body(items));
        return CurseForgeDirect.ParseFiles(document.RootElement).Items;
    }

    private static readonly string[] Mixed =
    [
        Item(1, "1.0", "Questie-1.0.zip", "2026-10-01T00:00:00Z"),
        Item(3, "1.2", "Questie-1.2.zip", "2026-10-03T00:00:00Z"),
        Item(2, "1.1", "Questie-1.1.zip", "2026-10-02T00:00:00Z"),
        Item(4, "1.3-beta", "Questie-1.3-beta.zip", "2026-10-04T00:00:00Z", releaseType: 2),
        Item(5, "1.4-alpha", "Questie-1.4-alpha.zip", "2026-10-05T00:00:00Z", releaseType: 3),
        Item(6, "9.9", "Questie-retail.zip", "2026-10-06T00:00:00Z", versionType: 517 + 1),
        Item(7, "8.8", "Questie-processing.zip", "2026-10-07T00:00:00Z", status: 1),
        Item(8, "7.7", "Questie-rejected.zip", "2026-10-08T00:00:00Z", status: 5),
    ];

    [Fact]
    public void Select_KeepsEarlyAccessAndReleasedFiles()
    {
        var files = Files(
            Item(1, "1.0", "a.zip", "2026-10-01T00:00:00Z"),
            Item(2, "1.1", "b.zip", "2026-10-02T00:00:00Z", earlyAccess: true),
            Item(3, "1.2", "c.zip", "2026-10-03T00:00:00Z", status: 10));

        Assert.Equal("1.2", CurseForgeDirect.Select(files, "release", VersionType)!.Version);
        Assert.Equal("1.1", CurseForgeDirect.Select(files.Where(file => file.Id != 3), "release", VersionType)!.Version);
    }

    [Fact]
    public void Select_MissingOrUnknownStatus_IsUnavailable()
    {
        using var document = JsonDocument.Parse(Body(
            Item(1, "1.0", "a.zip", "2026-10-01T00:00:00Z"),
            Item(2, "1.1", "b.zip", "2026-10-02T00:00:00Z", status: 99),
            Item(3, "1.2", "c.zip", "2026-10-03T00:00:00Z").Replace(",\"status\":4", "", StringComparison.Ordinal)));

        Assert.Equal("1.0", CurseForgeDirect.Select(CurseForgeDirect.ParseFiles(document.RootElement).Items, "release", VersionType)!.Version);
    }

    [Fact]
    public void ParseFiles_SkippedItem_LeavesTheListIncomplete()
    {
        using var whole = JsonDocument.Parse(Body(Item(1, "1.0", "a.zip", "2026-10-01T00:00:00Z")));
        using var skipped = JsonDocument.Parse(Body(Item(1, "1.0", "a.zip", "2026-10-01T00:00:00Z"), Item(0, "bad", "b.zip", "2026-10-02T00:00:00Z")));

        Assert.True(CurseForgeDirect.ParseFiles(whole.RootElement).Complete);
        Assert.False(CurseForgeDirect.ParseFiles(skipped.RootElement).Complete);
    }

    [Theory]
    [InlineData(0, "a.zip")]
    [InlineData(-1, "a.zip")]
    [InlineData(1, ".")]
    [InlineData(1, "..")]
    [InlineData(1, "a/b.zip")]
    [InlineData(1, "a\\b.zip")]
    [InlineData(1, "a\tb.zip")]
    public void ParseFiles_SkipsItemsWithABadIdOrFileName(long id, string fileName)
    {
        var files = Files(Item(id, "1.0", fileName, "2026-10-01T00:00:00Z"), Item(2, "1.1", "ok.zip", "2026-10-02T00:00:00Z"));

        Assert.Equal(2, Assert.Single(files).Id);
    }

    [Fact]
    public void Select_Release_TakesTheNewestReleaseByDateCreatedForTheVersionType()
    {
        Assert.Equal("1.2", CurseForgeDirect.Select(Files(Mixed), "release", VersionType)!.Version);
    }

    [Fact]
    public void Select_PreRelease_TakesBetaOrAlpha()
    {
        Assert.Equal("1.4-alpha", CurseForgeDirect.Select(Files(Mixed), "pre-release", VersionType)!.Version);
    }

    [Fact]
    public void Select_OtherVersionTypeOrChannel_FindsNothing()
    {
        Assert.Null(CurseForgeDirect.Select(Files(Mixed), "release", 1));
        Assert.Null(CurseForgeDirect.Select(Files(Mixed), "beta", VersionType));
    }

    [Fact]
    public void ParseFiles_NoDisplayName_FallsBackToFileName()
    {
        Assert.Equal("Questie-1.0.zip", Assert.Single(Files(Item(1, null, "Questie-1.0.zip", "2026-10-01T00:00:00Z"))).Version);
    }

    [Theory]
    [InlineData("""{"pagination":{}}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""[]""")]
    public void ParseFiles_BodyWithoutADataArray_Throws(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<JsonException>(() => CurseForgeDirect.ParseFiles(document.RootElement));
    }

    [Fact]
    public void ParseFiles_SkipsItemsMissingRequiredFields()
    {
        using var document = JsonDocument.Parse("""{"data":[{"id":1,"displayName":"1.0","fileName":"a.zip","dateCreated":"2026-10-01T00:00:00Z"},{"displayName":"1.0","fileName":"a.zip","dateCreated":"2026-10-01T00:00:00Z","gameVersionTypeIds":[1]}]}""");

        Assert.Empty(CurseForgeDirect.ParseFiles(document.RootElement).Items);
    }

    [Fact]
    public void ParseRequires_KeepsOnlyRequiredDependencies()
    {
        using var document = JsonDocument.Parse("""{"data":[{"id":3358,"name":"DBM","type":"RequiredDependency"},{"id":14328,"name":"LibStub","type":"EmbeddedLibrary"},{"id":5,"name":"X","type":"OptionalDependency"}]}""");

        var requirement = Assert.Single(CurseForgeDirect.ParseRequires(document.RootElement));

        Assert.Equal("curseforge:3358", requirement.Id);
        Assert.Equal("DBM", requirement.Name);
    }

    [Theory]
    [InlineData("https://api.hoobi.io/guild/api/addons/curseforge/334372/88568/", 334372, 88568)]
    [InlineData("https://api.hoobi.io/guild/api/addons/curseforge/334372/", null, null)]
    [InlineData("https://addon.hoobi.io/questie/", null, null)]
    [InlineData(null, null, null)]
    public void Ids_ReadsModAndVersionTypeFromTheManifestBaseUrl(string? url, int? modId, int? versionType)
    {
        Assert.Equal(modId is null ? null : (modId.Value, versionType!.Value), CurseForgeDirect.Ids(url));
    }

    [Fact]
    public void FilesUri_AsksForAlphaFiles()
    {
        Assert.Equal("https://www.curseforge.com/api/v1/mods/2382/files?pageIndex=0&pageSize=50&sort=dateCreated&sortDescending=true&removeAlphas=false", CurseForgeDirect.FilesUri(2382).AbsoluteUri);
    }

    [Fact]
    public void ParseFiles_CappedTotalCount_IsNeverComplete()
    {
        using var capped = JsonDocument.Parse(Body(Item(1, "1.0", "a.zip", "2026-10-01T00:00:00Z")).Replace("\"totalCount\":1", "\"totalCount\":10000", StringComparison.Ordinal));

        Assert.False(CurseForgeDirect.ParseFiles(capped.RootElement).Complete);
    }

    [Fact]
    public void ZipUrl_MatchesTheServersEdgePath()
    {
        Assert.Equal("https://edge.forgecdn.net/files/9150/393/Questie-v12.1.0%2Bv1.0.6.zip", CurseForgeDirect.ZipUrl(9150393, "Questie-v12.1.0+v1.0.6.zip"));
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Files { get; set; } = () => Json(Body(Mixed));

        public byte[] DirectZip { get; set; } = [];

        public byte[] ProxyZip { get; set; } = [];

        public List<Uri> Requests { get; } = [];

        public int DirectCalls => Requests.Count(uri => uri.Host == "www.curseforge.com" && uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal));

        public Func<HttpResponseMessage> Dependencies { get; set; } = () => Json("""{"data":[{"id":3358,"name":"DBM","type":"RequiredDependency"}]}""");

        public int DependencyCalls => Requests.Count(uri => uri.AbsolutePath.EndsWith("/dependencies", StringComparison.Ordinal));

        public int ProxyCalls => Requests.Count(uri => uri.Host == "api.example.com");

        public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (Requests)
            {
                Requests.Add(uri);
            }

            return Task.FromResult(uri switch
            {
                { Host: "www.curseforge.com" } when uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal) => Files(),
                { Host: "www.curseforge.com" } => Dependencies(),
                { Host: "api.example.com" } => Json(ProxyManifest.Replace("%SIZE%", ProxyZip.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)),
                { AbsolutePath: "/files/9/9/Proxy.zip" } => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ProxyZip) },
                { Host: "edge.forgecdn.net" } => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(DirectZip) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }
    }

    private static (AddonUpdater Updater, RoutingHandler Handler, FakeClock Clock) Setup()
    {
        var handler = new RoutingHandler();
        var clock = new FakeClock();
        return (new AddonUpdater(new HttpClient(handler), clock: clock), handler, clock);
    }

    [Fact]
    public async Task ProbeChannelsAsync_Direct_BuildsTheReleaseWithoutCallingTheProxy()
    {
        var (updater, handler, _) = Setup();

        var releases = await updater.ProbeChannelsAsync(Questie, ["release", "pre-release"], TestContext.Current.CancellationToken, direct: true);

        var release = releases["release"]!;
        Assert.True(release.Direct);
        Assert.Equal("1.2", release.Version);
        Assert.Equal("https://edge.forgecdn.net/files/0/3/Questie-1.2.zip", release.Zip);
        Assert.Equal(123, release.Size);
        Assert.Null(release.Sha256);
        Assert.Null(release.Folders);
        Assert.Equal("https://www.curseforge.com/wow/addons/questie", release.Website);
        Assert.Null(release.Requires);
        Assert.Equal("1.4-alpha", releases["pre-release"]!.Version);
        Assert.Equal(1, handler.DirectCalls);
        Assert.Equal(0, handler.DependencyCalls);
        Assert.Equal(0, handler.ProxyCalls);
    }

    [Fact]
    public async Task ProbeChannelsAsync_NoEligibleFileInAPartialList_AsksTheProxyForThatChannelOnly()
    {
        var (updater, handler, _) = Setup();
        handler.Files = () => RoutingHandler.Json(Body(Mixed).Replace($"\"totalCount\":{Mixed.Length}", "\"totalCount\":980", StringComparison.Ordinal));

        var releases = await updater.ProbeChannelsAsync(Questie, ["release", "beta"], TestContext.Current.CancellationToken, direct: true);
        var next = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        Assert.True(releases["release"]!.Direct);
        Assert.Equal("proxy-2", releases["beta"]!.Version);
        Assert.Equal(1, handler.ProxyCalls);
        Assert.True(next["release"]!.Direct);
    }

    [Fact]
    public async Task ProbeChannelsAsync_OnlyFileDroppedForItsStatus_AsksTheProxyForThatChannel()
    {
        var (updater, handler, _) = Setup();
        handler.Files = () => RoutingHandler.Json(Body(
            Item(1, "1.0", "a.zip", "2026-10-01T00:00:00Z"),
            Item(2, "1.1-beta", "b.zip", "2026-10-02T00:00:00Z", releaseType: 2, status: 1)));

        var releases = await updater.ProbeChannelsAsync(Questie, ["release", "pre-release"], TestContext.Current.CancellationToken, direct: true);

        Assert.True(releases["release"]!.Direct);
        Assert.Equal("proxy-2", releases["pre-release"]!.Version);
        Assert.Equal(1, handler.ProxyCalls);
    }

    [Fact]
    public async Task ProbeChannelsAsync_NoEligibleFileInACompleteList_IsNoRelease()
    {
        var (updater, handler, _) = Setup();

        var releases = await updater.ProbeChannelsAsync(Questie, ["beta"], TestContext.Current.CancellationToken, direct: true);

        Assert.Null(releases["beta"]);
        Assert.Equal(0, handler.ProxyCalls);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("notfound")]
    public async Task ProbeChannelsAsync_PerModProblem_FallsBackForThatAddonWithoutTheSkip(string kind)
    {
        var (updater, handler, _) = Setup();
        handler.Files = kind == "empty"
            ? () => RoutingHandler.Json(Body())
            : () => new HttpResponseMessage(HttpStatusCode.NotFound);

        var releases = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);
        handler.Files = () => RoutingHandler.Json(Body(Mixed));
        var next = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        Assert.Equal("proxy-2", releases["release"]!.Version);
        Assert.True(next["release"]!.Direct);
    }

    [Fact]
    public async Task WithDirectRequiresAsync_FetchesTheRequiredDependencies()
    {
        var (updater, handler, _) = Setup();
        var releases = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        var release = await updater.WithDirectRequiresAsync(Questie, "release", releases["release"]!, TestContext.Current.CancellationToken);

        Assert.True(release.Direct);
        Assert.Equal("curseforge:3358", Assert.Single(release.Requires!).Id);
        Assert.Equal(1, handler.DependencyCalls);
        Assert.Equal(0, handler.ProxyCalls);
    }

    [Fact]
    public async Task WithDirectRequiresAsync_DependenciesFail_UsesTheProxyReleaseAndKeepsDirectForOthers()
    {
        var (updater, handler, _) = Setup();
        handler.Dependencies = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var releases = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        var release = await updater.WithDirectRequiresAsync(Questie, "release", releases["release"]!, TestContext.Current.CancellationToken);
        var next = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        Assert.Equal("proxy-2", release.Version);
        Assert.False(release.Direct);
        Assert.True(next["release"]!.Direct);
    }

    [Fact]
    public async Task ProbeChannelsAsync_NotDirect_CallsOnlyTheProxy()
    {
        var (updater, handler, _) = Setup();

        var releases = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken);

        Assert.Equal("proxy-2", releases["release"]!.Version);
        Assert.Equal(0, handler.DirectCalls);
    }

    public static TheoryData<string> FailureKinds => ["status", "timeout", "garbage", "nodata", "noversiontypes"];

    private static Func<HttpResponseMessage> Failure(string kind) => kind switch
    {
        "status" => () => new HttpResponseMessage(HttpStatusCode.Forbidden),
        "timeout" => () => throw new TaskCanceledException("timed out"),
        "garbage" => () => RoutingHandler.Json("<html>Just a moment...</html>"),
        "nodata" => () => RoutingHandler.Json("""{"error":"x"}"""),
        _ => () => RoutingHandler.Json("""{"data":[{"id":1,"displayName":"1.0","fileName":"a.zip","dateCreated":"2026-10-01T00:00:00Z"}]}"""),
    };

    [Theory]
    [MemberData(nameof(FailureKinds))]
    public async Task ProbeChannelsAsync_DirectFails_FallsBackToTheProxy(string kind)
    {
        var (updater, handler, _) = Setup();
        handler.Files = Failure(kind);

        var releases = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        Assert.Equal("proxy-2", releases["release"]!.Version);
        Assert.False(releases["release"]!.Direct);
        Assert.Equal(1, handler.ProxyCalls);
    }

    [Fact]
    public async Task ProbeChannelsAsync_AfterADirectFailure_SkipsDirectForAnHour()
    {
        var (updater, handler, clock) = Setup();
        handler.Files = Failure("status");
        await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);
        handler.Files = () => RoutingHandler.Json(Body(Mixed));

        clock.Now += TimeSpan.FromMinutes(59);
        var skipped = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);
        clock.Now += TimeSpan.FromMinutes(2);
        var resumed = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        Assert.False(skipped["release"]!.Direct);
        Assert.True(resumed["release"]!.Direct);
        Assert.Equal(2, handler.DirectCalls);
    }

    private byte[] Zip(params string[] entries)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var name in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write("## Version: 2");
            }
        }

        return File.ReadAllBytes(path);
    }

    [Fact]
    public async Task InstallRefetchingOn404Async_DirectSizeMismatch_InstallsTheProxyReleaseWithoutTheSkip()
    {
        var (updater, handler, _) = Setup();
        handler.DirectZip = Zip("Questie/Questie.toc");
        handler.ProxyZip = Zip("Questie/Questie.toc", "Questie/Proxy.lua");
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        var releases = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        var (_, installed) = await updater.InstallRefetchingOn404Async(Questie, "release", releases["release"]!, addOnsPath, null, TestContext.Current.CancellationToken);
        var after = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        Assert.Equal("proxy-2", installed.Version);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "Questie", "Proxy.lua")));
        Assert.False(after["release"]!.Direct);
        Assert.Equal(2, handler.DirectCalls);
    }

    [Fact]
    public async Task InstallRefetchingOn404Async_DirectInstallSucceeds_NeverCallsTheProxy()
    {
        var (updater, handler, _) = Setup();
        handler.DirectZip = Zip("Questie/Questie.toc");
        handler.Files = () => RoutingHandler.Json(Body(Item(3, "1.2", "Questie-1.2.zip", "2026-10-03T00:00:00Z").Replace("\"fileLength\":123", $"\"fileLength\":{handler.DirectZip.Length}", StringComparison.Ordinal)));
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        var releases = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        var (folders, installed) = await updater.InstallRefetchingOn404Async(Questie, "release", releases["release"]!, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal("1.2", installed.Version);
        Assert.Equal(["Questie"], folders);
        Assert.Equal(0, handler.ProxyCalls);
    }

    [Fact]
    public async Task InstallRefetchingOn404Async_ProxyInstallsAnotherVersion_DirectNeverOffersThatFileAgain()
    {
        var (updater, handler, _) = Setup();
        handler.DirectZip = Zip("Questie/Questie.toc");
        handler.ProxyZip = Zip("Questie/Questie.toc");
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        var releases = await updater.ProbeChannelsAsync(Questie, ["release", "pre-release"], TestContext.Current.CancellationToken, direct: true);

        await updater.InstallRefetchingOn404Async(Questie, "release", releases["release"]!, addOnsPath, null, TestContext.Current.CancellationToken);
        var after = await updater.ProbeChannelsAsync(Questie, ["release", "pre-release"], TestContext.Current.CancellationToken, direct: true);

        Assert.Equal("proxy-2", after["release"]!.Version);
        Assert.False(after["release"]!.Direct);
        Assert.True(after["pre-release"]!.Direct);
        Assert.Equal(2, handler.DirectCalls);
    }

    [Fact]
    public async Task InstallRefetchingOn404Async_DirectZipFolderWithoutAToc_FallsBackOnceAndDirectIsNotTriedAgain()
    {
        var (updater, handler, _) = Setup();
        handler.DirectZip = Zip("Questie/Questie.toc", "Lib/Lib.lua");
        handler.ProxyZip = Zip("Questie/Questie.toc", "Lib/Lib.lua");
        handler.Files = () => RoutingHandler.Json(Body(Item(3, "proxy-2", "Questie-1.2.zip", "2026-10-03T00:00:00Z").Replace("\"fileLength\":123", $"\"fileLength\":{handler.DirectZip.Length}", StringComparison.Ordinal)));
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        var releases = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        var (folders, installed) = await updater.InstallRefetchingOn404Async(Questie, "release", releases["release"]!, addOnsPath, null, TestContext.Current.CancellationToken);
        var after = await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        Assert.True(releases["release"]!.Direct);
        Assert.Equal("proxy-2", installed.Version);
        Assert.Equal(["Questie", "Lib"], folders);
        Assert.False(after["release"]!.Direct);
        Assert.Equal(1, handler.Requests.Count(uri => uri.Host == "edge.forgecdn.net" && uri.AbsolutePath != "/files/9/9/Proxy.zip"));
    }

    [Fact]
    public async Task InstallRefetchingOn404Async_DirectReleaseHeldDuringTheSkip_InstallsFromTheProxyWithoutDownloadingIt()
    {
        var (updater, handler, _) = Setup();
        handler.ProxyZip = Zip("Questie/Questie.toc");
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);
        var held = (await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true))["release"]!;
        handler.Files = Failure("status");
        await updater.ProbeChannelsAsync(Questie, ["release"], TestContext.Current.CancellationToken, direct: true);

        var (_, installed) = await updater.InstallRefetchingOn404Async(Questie, "release", held, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal("proxy-2", installed.Version);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsoluteUri == held.Zip);
    }
}
