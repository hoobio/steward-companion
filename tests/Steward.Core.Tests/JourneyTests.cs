using System.IO.Compression;
using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class JourneyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "steward-journey-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body, TimeSpan? retryAfter = null) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (retryAfter is { } delay)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
            }

            return response;
        }
    }

    private static string LuaEscape(byte[] bytes)
    {
        var builder = new StringBuilder();
        foreach (var b in bytes)
        {
            builder.Append(b switch
            {
                0 => "\\000",
                (byte)'\n' => "\\n",
                (byte)'\r' => "\\r",
                (byte)'"' => "\\\"",
                (byte)'\\' => "\\\\",
                _ => ((char)b).ToString(),
            });
        }

        return builder.ToString();
    }

    private static byte[] Gzip(string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }

        return output.ToArray();
    }

    [Fact]
    public void Quality_ExpandsTrackRows()
    {
        using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "journey-tracks.json")));
        var packed = fixture.RootElement.GetProperty("packed").GetRawText();
        var text = "\r\nStewardJourneyDB = {\r\n\t[\"packed\"] = {\r\n\t\t{\r\n\t\t\t[\"to\"] = 172,\r\n\t\t\t[\"data\"] = \"" + LuaEscape(Gzip(packed)) + "\",\r\n\t\t\t[\"rows\"] = 11,\r\n\t\t\t[\"from\"] = 100,\r\n\t\t},\r\n\t},\r\n}\r\n";

        var quality = JourneyFile.Quality(Encoding.Latin1.GetBytes(text));

        Assert.Equal(9, quality.Positions);
        Assert.Equal(72, quality.ActiveSeconds);
        Assert.Equal(1, quality.Subzones);
    }

    private static byte[] Fixture(bool routeGuid = true)
    {
        var packed = LuaEscape(Gzip("""[{"ev":"login","guid":"Player-5-OLD","t":100},{"ev":"login","guid":"Player-5-PACKED","t":150},{"ev":"pos","t":190}]"""));
        var login = routeGuid ? "\t\t\t[\"guid\"] = \"Player-5-0ABC\",\r\n" : "";
        var text =
            "\r\nStewardJourneyDB = {\r\n\t[\"route\"] = {\r\n"
            + "\t\t{\r\n\t\t\t[\"ev\"] = \"login\",\r\n" + login + "\t\t\t[\"t\"] = 300,\r\n\t\t\t[\"zone\"] = \"TeldÃ(rassil\",\r\n\t\t},\r\n"
            + "\t\t{\r\n\t\t\t[\"ev\"] = \"pos\",\r\n\t\t\t[\"t\"] = 310,\r\n\t\t},\r\n"
            + "\t\t{\r\n\t\t\t[\"ev\"] = \"xp\",\r\n\t\t\t[\"t\"] = 320,\r\n\t\t},\r\n\t},\r\n"
            + "\t[\"packed\"] = {\r\n\t\t{\r\n\t\t\t[\"to\"] = 200,\r\n\t\t\t[\"lvl\"] = 1,\r\n\t\t\t[\"data\"] = \"" + packed + "\",\r\n"
            + "\t\t\t[\"rows\"] = 9,\r\n\t\t\t[\"from\"] = 50,\r\n\t\t},\r\n\t},\r\n}\r\n";
        return Encoding.Latin1.GetBytes(text);
    }

    private string WriteFile(byte[] bytes, string character = "Hoobi-Doofi")
    {
        var path = Path.Combine(_root, "WTF", "Account", "54939295#1", "70", character, "SavedVariables", JourneyFile.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        File.WriteAllBytes(path + ".bak", bytes);
        return path;
    }

    private (JourneyUploader Uploader, RecordingHandler Handler, AppStateStore Store) UploaderFor(
        HttpStatusCode status, string body = """{"result":"added","sha256":"x"}""", TimeSpan? retryAfter = null, Func<DateTimeOffset>? clock = null, bool anyLength = true)
    {
        var handler = new RecordingHandler(status, body, retryAfter);
        var client = new StewardClient(new HttpClient(handler), "https://api.example.com/guild", "Steward/1.0.0 (dev)");
        var store = new AppStateStore([], Path.Combine(_root, "state", "state.json"));
        return (new JourneyUploader(client, store, clock, anyLength ? _ => true : null, launch: 0), handler, store);
    }

    private static string Ungzip(byte[] body)
    {
        using var gzip = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.Latin1.GetString(output.ToArray());
    }

    private static byte[] TwoChunks(string extra = "")
    {
        static string Chunk(int to) =>
            "\t\t{\r\n\t\t\t[\"rows\"] = 1,\r\n\t\t\t[\"from\"] = " + to + ",\r\n\t\t\t[\"to\"] = " + to + ",\r\n\t\t\t[\"data\"] = \"" + LuaEscape(Gzip("[{\"ev\":\"pos\",\"t\":" + to + "}]")) + "\",\r\n\t\t},\r\n";
        return Encoding.Latin1.GetBytes(
            "StewardJourneyDB = {\r\n\t[\"jumps\"] = 7,\r\n\t[\"route\"] = {\r\n\t\t{\r\n\t\t\t[\"ev\"] = \"pos\",\r\n\t\t\t[\"sub\"] = \"TeldÃ¢\",\r\n\t\t\t[\"t\"] = 300,\r\n\t\t},\r\n" + extra + "\t},\r\n"
            + "\t[\"packed\"] = {\r\n" + Chunk(100) + Chunk(200) + "\t},\r\n}\r\n");
    }

    private static string[] ChunkHashes(byte[] bytes) =>
        [.. LuaSavedVariables.Parse(Encoding.Latin1.GetString(bytes))["StewardJourneyDB"].GetTable("packed")!.Items.Select(chunk => JourneyFile.ChunkHash(chunk.GetString("data")!))];

    [Fact]
    public async Task Upload_SendsChunksTheServerListedByHashOnTheNextUpload()
    {
        var first = TwoChunks();
        var hashes = ChunkHashes(first);
        var path = WriteFile(first);
        var clock = DateTimeOffset.UtcNow;
        var (uploader, handler, store) = UploaderFor(HttpStatusCode.OK, $$"""{"result":"added","sha256":"x","chunks":["{{hashes[0]}}"]}""", clock: () => clock);

        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.False(handler.Requests[0].Request.Headers.Contains("X-Journey-Refs"));
        Assert.Equal(first.Length, Ungzip(handler.Requests[0].Body).Length);
        Assert.Equal(hashes[..1], store.Load().JourneyUploads[path].Chunks);

        File.WriteAllBytes(path, TwoChunks("\t\t{\r\n\t\t\t[\"t\"] = 310,\r\n\t\t},\r\n"));
        clock = clock.AddMinutes(1);
        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Equal("1", handler.Requests[1].Request.Headers.GetValues("X-Journey-Refs").Single());
        Assert.Equal("4", handler.Requests[1].Request.Headers.GetValues("X-Journey-Rows").Single());
        var sent = Ungzip(handler.Requests[1].Body);
        Assert.Contains("[\"ref\"] = \"x\"", sent);
        Assert.Equal(2, LuaSavedVariables.Parse(sent)["StewardJourneyDB"].GetTable("packed")!.Items.Count);
        Assert.Single(LuaSavedVariables.Parse(sent)["StewardJourneyDB"].GetTable("packed")!.Items, chunk => chunk.GetString("data") is not null);
        Assert.Equal(7, LuaSavedVariables.Parse(sent)["StewardJourneyDB"].GetNumber("jumps"));
        Assert.Equal("TeldÃ¢", LuaSavedVariables.Parse(sent)["StewardJourneyDB"].GetTable("route")!.Items[0].GetString("sub"));
    }

    [Fact]
    public async Task Upload_Resend_ForgetsTheHashesAndSendsTheWholeFileNext()
    {
        var bytes = TwoChunks();
        var hash = ChunkHashes(bytes)[0];
        var path = WriteFile(bytes);
        var clock = DateTimeOffset.UtcNow;
        var (uploader, handler, store) = UploaderFor(HttpStatusCode.OK, """{"result":"resend"}""", clock: () => clock);
        var state = store.Load();
        state.JourneyUploads[path] = new JourneyUploadRecord("old", 1, default, "added", null, Chunks: [hash], StoredSha256: "stored");
        store.Save(state);

        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Equal("1", handler.Requests[0].Request.Headers.GetValues("X-Journey-Refs").Single());
        Assert.Equal("old", store.Load().JourneyUploads[path].Sha256);
        Assert.Null(store.Load().JourneyUploads[path].Chunks);

        clock = clock.AddMinutes(1);
        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.False(handler.Requests[1].Request.Headers.Contains("X-Journey-Refs"));
    }

    [Fact]
    public void WithRef_LeavesOutHeldChunksAndSendsAWholeFileWhenOneIsGone()
    {
        var bytes = TwoChunks();
        var hashes = ChunkHashes(bytes);

        var (all, refs) = JourneyFile.WithRef(bytes, hashes.ToHashSet(), "stored");
        var packed = LuaSavedVariables.Parse(Encoding.Latin1.GetString(all))["StewardJourneyDB"].GetTable("packed")!.Items;
        Assert.Equal(1, refs);
        Assert.Equal("stored", Assert.Single(packed).GetString("ref"));

        var (whole, none) = JourneyFile.WithRef(bytes, new HashSet<string> { hashes[0], "gone" }, "stored");
        Assert.Same(bytes, whole);
        Assert.Equal(0, none);
    }

    [Fact]
    public void FromLaunch_DropsChunksThatEndBeforeTheLaunch()
    {
        var bytes = TwoChunks();
        Assert.Same(bytes, JourneyFile.FromLaunch(bytes, 100));

        var trimmed = JourneyFile.FromLaunch(bytes, 101);
        Assert.Equal(new JourneySummary(2, 200, 300, null), JourneyFile.Summarise(trimmed));
        Assert.Single(LuaSavedVariables.Parse(Encoding.Latin1.GetString(trimmed))["StewardJourneyDB"].GetTable("packed")!.Items);
    }

    [Fact]
    public async Task Upload_FileOfOnlyPreLaunchChunks_IsNotSent()
    {
        var path = WriteFile(Encoding.Latin1.GetBytes("StewardJourneyDB = {\r\n\t[\"packed\"] = {\r\n\t\t{\r\n\t\t\t[\"to\"] = 100,\r\n\t\t\t[\"rows\"] = 1,\r\n\t\t\t[\"data\"] = \"x\",\r\n\t\t},\r\n\t},\r\n}\r\n"));
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"result":"added"}""", null);
        var store = new AppStateStore([], Path.Combine(_root, "state", "state.json"));
        var uploader = new JourneyUploader(new StewardClient(new HttpClient(handler), "https://api.example.com/guild", "Steward/1.0.0 (dev)"), store, launch: 101);

        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.Equal(JourneyUploader.TooShort, store.Load().JourneyUploads[path].Result);
    }

    [Theory]
    [InlineData(3600, 3, true)]
    [InlineData(3599, 3, false)]
    [InlineData(7200, 2, false)]
    public void A_journey_is_worth_uploading_from_an_hour_and_three_subzones(long active, int subzones, bool expected) =>
        Assert.Equal(expected, new JourneyQuality(subzones, active, 0, "").IsWorthUploading);

    [Fact]
    public async Task Upload_ShortJourney_IsSkippedUntilTheFileChanges()
    {
        var path = WriteFile(QualityFixture());
        var (uploader, handler, store) = UploaderFor(HttpStatusCode.OK, anyLength: false);

        await uploader.UploadAsync([_root], CancellationToken.None);
        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Empty(handler.Requests);
        var record = store.Load().JourneyUploads[path];
        Assert.Equal(JourneyUploader.TooShort, record.Result);
        Assert.Null(record.UploadedAt);
    }

    [Fact]
    public void AccountHash_IsTheFirst16HexOfSha256()
    {
        Assert.Equal("18824df56be41b38", JourneyFile.AccountHash("54939295#1"));
    }

    [Fact]
    public void Summarise_CountsRouteAndPackedRowsAndTakesTheRouteLoginGuid()
    {
        var summary = JourneyFile.Summarise(Fixture());

        Assert.Equal(new JourneySummary(12, 50, 320, "Player-5-0ABC"), summary);
    }

    [Fact]
    public void Summarise_FallsBackToTheNewestPackedChunksLastLoginGuid()
    {
        Assert.Equal("Player-5-PACKED", JourneyFile.Summarise(Fixture(routeGuid: false)).CharacterGuid);
    }

    [Fact]
    public void Summarise_NoTable_IsZeroRows()
    {
        Assert.Equal(new JourneySummary(0, null, null, null), JourneyFile.Summarise(Encoding.Latin1.GetBytes("StewardJourneyDB = nil\r\n")));
    }

    private static byte[] QualityFixture()
    {
        var packed = LuaEscape(Gzip("""[{"ev":"pos","t":100,"sub":"C","x":0.1,"y":0.2},{"ev":"pos","t":200,"sub":"A"},{"ev":"login","t":250,"addon":"0.0.1"}]"""));
        const string route =
            "\t\t{ [\"ev\"] = \"login\", [\"t\"] = 1000, [\"sub\"] = \"A\", [\"x\"] = 0.5, [\"y\"] = 0.5, [\"addon\"] = \"0.1.0-pre-release.ab12;x\" },\r\n"
            + "\t\t{ [\"ev\"] = \"pos\", [\"t\"] = 1100, [\"sub\"] = \"B\", [\"x\"] = 0.5, [\"y\"] = 0.5 },\r\n"
            + "\t\t{ [\"ev\"] = \"pos\", [\"t\"] = 2000, [\"sub\"] = \"A\", [\"x\"] = 0.5 },\r\n"
            + "\t\t{ [\"ev\"] = \"pos\", [\"t\"] = 2100, [\"sub\"] = \"\" },\r\n";
        var text =
            "StewardJourneyDB = {\r\n\t[\"route\"] = {\r\n" + route + "\t},\r\n"
            + "\t[\"packed\"] = {\r\n\t\t{ [\"to\"] = 250, [\"lvl\"] = 1, [\"rows\"] = 3, [\"from\"] = 100, [\"data\"] = \"" + packed + "\" },\r\n\t},\r\n}\r\n";
        return Encoding.Latin1.GetBytes(text);
    }

    [Fact]
    public void Quality_CountsSubzonesActiveTimePositionsAndTheNewestLoginsAddon()
    {
        var quality = JourneyFile.Quality(QualityFixture());

        Assert.Equal(new JourneyQuality(3, 350, 3, "0.1.0-pre-release.ab12;x"), quality);
        Assert.Equal("subzones=3;active=350;positions=3;addon=0.1.0-pre-release.ab12x", quality.Header);
    }

    [Fact]
    public void Quality_CountsANonAsciiSubzoneOnceAndSkipsRowsWithoutATime()
    {
        var packed = LuaEscape(Gzip("""[{"ev":"pos","t":100,"sub":"Café"}]"""));
        var cafeAsWritten = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("Café"));
        var text = "StewardJourneyDB = {\r\n\t[\"route\"] = {\r\n"
            + "\t\t{ [\"ev\"] = \"pos\", [\"t\"] = 200, [\"sub\"] = \"" + cafeAsWritten + "\" },\r\n"
            + "\t\t{ [\"ev\"] = \"pos\", [\"sub\"] = \"Untimed\" },\r\n\t},\r\n"
            + "\t[\"packed\"] = {\r\n\t\t{ [\"to\"] = 100, [\"rows\"] = 1, [\"from\"] = 100, [\"data\"] = \"" + packed + "\" },\r\n\t},\r\n}\r\n";

        Assert.Equal(1, JourneyFile.Quality(Encoding.Latin1.GetBytes(text)).Subzones);
    }

    [Fact]
    public void Quality_NoTable_IsEmpty()
    {
        Assert.Equal("subzones=0;active=0;positions=0;addon=", JourneyFile.Quality(Encoding.Latin1.GetBytes("StewardJourneyDB = nil\r\n")).Header);
    }

    [Fact]
    public async Task Upload_SendsTheQualityHeader()
    {
        WriteFile(QualityFixture());
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.OK);

        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Equal("subzones=3;active=350;positions=3;addon=0.1.0-pre-release.ab12x", Assert.Single(handler.Requests).Request.Headers.GetValues("X-Journey-Quality").Single());
    }

    [Fact]
    public async Task Upload_SendsTheGzippedFileWithHeadersAndRecordsIt()
    {
        var bytes = Fixture();
        var path = WriteFile(bytes);
        var (uploader, handler, store) = UploaderFor(HttpStatusCode.OK);

        var result = await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Equal(JourneyPassResult.Done, result);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api.example.com/guild/api/sync/journey/18824df56be41b38/70/Hoobi-Doofi", request.RequestUri!.ToString());
        Assert.Equal("application/octet-stream", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(["gzip"], request.Content.Headers.ContentEncoding);
        Assert.Equal("12", request.Headers.GetValues("X-Journey-Rows").Single());
        Assert.Equal("50", request.Headers.GetValues("X-Journey-First").Single());
        Assert.Equal("320", request.Headers.GetValues("X-Journey-Last").Single());
        Assert.Equal("Player-5-0ABC", request.Headers.GetValues("X-Journey-Guid").Single());
        Assert.Equal(
            new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Headers.GetValues("X-Journey-Mtime").Single());
        using var gunzip = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        gunzip.CopyTo(decompressed);
        Assert.Equal(bytes, decompressed.ToArray());

        var record = store.Load().JourneyUploads[path];
        Assert.Equal("added", record.Result);
        Assert.Null(record.Error);
    }

    [Fact]
    public async Task Upload_UnchangedFile_SkipsOnSizeAndMtimeThenOnHash()
    {
        var path = WriteFile(Fixture());
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.OK);

        await uploader.UploadAsync([_root], CancellationToken.None);
        await uploader.UploadAsync([_root], CancellationToken.None);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Upload_ChangedFile_UploadsAgain()
    {
        var path = WriteFile(Fixture());
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.OK);

        await uploader.UploadAsync([_root], CancellationToken.None);
        File.WriteAllBytes(path, Fixture(routeGuid: false));
        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Upload_ZeroRows_IsSkipped()
    {
        WriteFile(Encoding.Latin1.GetBytes("StewardJourneyDB = {\r\n[\"route\"] = {},\r\n}\r\n"));
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.OK);

        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Upload_NotFound_BlocksUntilUnblocked()
    {
        WriteFile(Fixture());
        WriteFile(Fixture(), "Hoobi-Furry");
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.NotFound, "");

        await uploader.UploadAsync([_root], CancellationToken.None);
        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.True(uploader.IsBlocked);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Upload_Throttled_WaitsOutRetryAfter()
    {
        WriteFile(Fixture());
        var now = DateTimeOffset.UnixEpoch;
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.TooManyRequests, "", TimeSpan.FromSeconds(30), () => now);

        await uploader.UploadAsync([_root], CancellationToken.None);
        now += TimeSpan.FromSeconds(10);
        await uploader.UploadAsync([_root], CancellationToken.None);
        now += TimeSpan.FromSeconds(30);
        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Upload_Unauthorized_ReportsSessionExpired()
    {
        WriteFile(Fixture());
        var (uploader, _, _) = UploaderFor(HttpStatusCode.Unauthorized, "");

        Assert.Equal(JourneyPassResult.SessionExpired, await uploader.UploadAsync([_root], CancellationToken.None));
    }

    [Fact]
    public async Task Upload_RejectedFile_IsNotResentUntilItChanges()
    {
        WriteFile(Fixture());
        var (uploader, handler, store) = UploaderFor(HttpStatusCode.RequestEntityTooLarge, "");

        await uploader.UploadAsync([_root], CancellationToken.None);
        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.Equal("The journey file is too large to upload.", Assert.Single(store.Load().JourneyUploads.Values).Error);
    }

    [Fact]
    public async Task Upload_SavesWithin30Seconds_CoalesceIntoOneUploadOfTheLatestFile()
    {
        var path = WriteFile(Fixture());
        var now = DateTimeOffset.UnixEpoch;
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.OK, clock: () => now);
        byte[] latest = [.. Fixture(), (byte)'\n', (byte)'\n'];

        await uploader.UploadAsync([_root], CancellationToken.None);
        now += TimeSpan.FromSeconds(10);
        File.WriteAllBytes(path, [.. Fixture(), (byte)'\n']);
        await uploader.UploadAsync([_root], CancellationToken.None);
        now += TimeSpan.FromSeconds(10);
        File.WriteAllBytes(path, latest);
        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Single(handler.Requests);

        now += TimeSpan.FromSeconds(11);
        await uploader.UploadAsync([_root], CancellationToken.None);
        await uploader.UploadAsync([_root], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        using var gunzip = new GZipStream(new MemoryStream(handler.Requests[1].Body), CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        gunzip.CopyTo(decompressed);
        Assert.Equal(latest, decompressed.ToArray());
    }

    [Fact]
    public async Task Upload_TwoFoldersWithOneGuid_AreSpacedLikeTheServerLimiter()
    {
        WriteFile(Fixture());
        WriteFile(Fixture(), "Hoobi-Renamed");
        var now = DateTimeOffset.UnixEpoch;
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.OK, clock: () => now);

        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Single(handler.Requests);

        now += TimeSpan.FromSeconds(31);
        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Upload_Throttled_IsNoErrorWaitsOutRetryAfterAndLetsOtherCharactersThrough()
    {
        WriteFile(Fixture());
        WriteFile(Fixture(routeGuid: false), "Hoobi-Furry");
        var now = DateTimeOffset.UnixEpoch;
        var (uploader, handler, store) = UploaderFor(HttpStatusCode.TooManyRequests, "", TimeSpan.FromSeconds(45), () => now);

        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(store.Load().JourneyUploads.Values, record => Assert.Null(record.Error));

        now += TimeSpan.FromSeconds(31);
        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);

        now += TimeSpan.FromSeconds(15);
        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Upload_ThrottledWithoutRetryAfter_WaitsAMinute()
    {
        WriteFile(Fixture());
        var now = DateTimeOffset.UnixEpoch;
        var (uploader, handler, _) = UploaderFor(HttpStatusCode.TooManyRequests, "", clock: () => now);

        await uploader.UploadAsync([_root], CancellationToken.None);
        now += TimeSpan.FromSeconds(59);
        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Single(handler.Requests);

        now += TimeSpan.FromSeconds(2);
        await uploader.UploadAsync([_root], CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }
}
