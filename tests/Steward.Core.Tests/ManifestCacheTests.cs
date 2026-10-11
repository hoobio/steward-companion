using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class ManifestCacheTests
{
    private static readonly Uri Manifest = new("https://addon.example/steward/latest-release.json");

    private const string ReleaseJson = """{"version":"1.2.3","zip":"steward-1.2.3.zip","sha256":"abc","size":10,"released":"2026-10-01T00:00:00Z"}""";

    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<string?> IfNoneMatch { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IfNoneMatch.Add(request.Headers.TryGetValues("If-None-Match", out var values) ? string.Join(",", values) : null);
            return Task.FromResult(responses[Math.Min(_next++, responses.Length - 1)](request));
        }
    }

    private static HttpResponseMessage Ok(string body, string? etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        return response;
    }

    private static HttpResponseMessage NotModified() => new(HttpStatusCode.NotModified);

    [Fact]
    public async Task SecondFetch_SendsTheETagAndReusesTheManifestOn304()
    {
        var handler = new ScriptedHandler(_ => Ok(ReleaseJson, "\"v1\""), _ => NotModified());
        using var client = new HttpClient(handler);
        var cache = new ManifestCache();

        var first = await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);
        var second = await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);

        Assert.Equal([null, "\"v1\""], handler.IfNoneMatch);
        Assert.Equal("1.2.3", first!.Version);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ChangedManifest_ReplacesTheCachedOne()
    {
        var handler = new ScriptedHandler(
            _ => Ok(ReleaseJson, "\"v1\""),
            _ => Ok(ReleaseJson.Replace("1.2.3", "1.2.4", StringComparison.Ordinal), "\"v2\""),
            _ => NotModified());
        using var client = new HttpClient(handler);
        var cache = new ManifestCache();

        await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);
        await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);
        var third = await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);

        Assert.Equal([null, "\"v1\"", "\"v2\""], handler.IfNoneMatch);
        Assert.Equal("1.2.4", third!.Version);
    }

    [Fact]
    public async Task ServerWithoutETags_IsFetchedInFullEveryTime()
    {
        var handler = new ScriptedHandler(_ => Ok(ReleaseJson, null));
        using var client = new HttpClient(handler);
        var cache = new ManifestCache();

        await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);
        var second = await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);

        Assert.Equal([null, null], handler.IfNoneMatch);
        Assert.Equal("1.2.3", second!.Version);
    }

    [Fact]
    public async Task EmptyChannel_NullManifestIsCachedToo()
    {
        var handler = new ScriptedHandler(_ => Ok("null", "\"empty\""), _ => NotModified());
        using var client = new HttpClient(handler);
        var cache = new ManifestCache();

        await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);
        var second = await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);

        Assert.Equal([null, "\"empty\""], handler.IfNoneMatch);
        Assert.Null(second);
    }

    [Fact]
    public async Task NotModifiedWithNothingCached_RefetchesUnconditionally()
    {
        var handler = new ScriptedHandler(_ => NotModified(), _ => Ok(ReleaseJson, "\"v1\""));
        using var client = new HttpClient(handler);

        var release = await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: new ManifestCache());

        Assert.Equal([null, null], handler.IfNoneMatch);
        Assert.Equal("1.2.3", release!.Version);
    }

    [Fact]
    public async Task ClearedCache_SendsNoETag()
    {
        var handler = new ScriptedHandler(_ => Ok(ReleaseJson, "\"v1\""));
        using var client = new HttpClient(handler);
        var cache = new ManifestCache();

        await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);
        cache.Clear();
        await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken, cache: cache);

        Assert.Equal([null, null], handler.IfNoneMatch);
    }

    [Fact]
    public async Task WithoutACache_NoETagIsEverSent()
    {
        var handler = new ScriptedHandler(_ => Ok(ReleaseJson, "\"v1\""));
        using var client = new HttpClient(handler);

        await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken);
        await AddonUpdater.FetchManifestAsync(client, Manifest, TestContext.Current.CancellationToken);

        Assert.Equal([null, null], handler.IfNoneMatch);
    }

    [Fact]
    public async Task GetLatest_RevalidatesThroughTheUpdaterCacheUnlessAskedNotTo()
    {
        var handler = new ScriptedHandler(_ => Ok(ReleaseJson, "\"v1\""));
        using var client = new HttpClient(handler);
        var updater = new AddonUpdater(client);
        var addon = new ManagedAddon("steward", "Steward", "https://addon.example/steward/");

        await updater.GetLatestAsync(addon, "release", TestContext.Current.CancellationToken);
        await updater.GetLatestAsync(addon, "release", TestContext.Current.CancellationToken);
        await updater.GetLatestAsync(addon, "release", TestContext.Current.CancellationToken, revalidate: false);

        Assert.Equal([null, "\"v1\"", null], handler.IfNoneMatch);
    }
}
