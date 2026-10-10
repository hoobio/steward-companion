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
        HttpStatusCode status, string body = """{"result":"added","sha256":"x"}""", TimeSpan? retryAfter = null, Func<DateTimeOffset>? clock = null)
    {
        var handler = new RecordingHandler(status, body, retryAfter);
        var client = new StewardClient(new HttpClient(handler), "https://api.example.com/guild", "Steward/1.0.0 (dev)");
        var store = new AppStateStore([], Path.Combine(_root, "state", "state.json"));
        return (new JourneyUploader(client, store, clock), handler, store);
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
}
