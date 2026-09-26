using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class AddonManifestTests
{
    private static readonly ManagedAddon Addon =
        new("hoobiscripts", "HoobiScripts", "https://addon.hoobi.io/hoobiscripts/");

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastRequest = request;
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (AddonUpdater Updater, StubHandler Handler) UpdaterFor(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new AddonUpdater(new HttpClient(handler)), handler);
    }

    private sealed class RoutingStubHandler(Func<Uri, (HttpStatusCode Status, string Body)> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = route(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static AddonUpdater UpdaterFor(Func<Uri, (HttpStatusCode Status, string Body)> route) =>
        new(new HttpClient(new RoutingStubHandler(route)));

    [Fact]
    public async Task GetLatestAsync_EmptyChannelPublishesNull_ReturnsNull()
    {
        var (updater, _) = UpdaterFor(HttpStatusCode.OK, "null");

        var release = await updater.GetLatestAsync(Addon, "pre-release", CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task GetLatestAsync_PopulatedChannel_DeserialisesTheRelease()
    {
        const string body = """
        {"version":"0.5.0-pre-release.e73f4bf","channel":"pre-release","zip":"HoobiScripts-0.5.0-pre-release.e73f4bf.zip",
         "sha256":"fad9149904b658d1f066f4f41c9301f8f18070249c44b5a3480bb558aefd0983","size":26222,
         "released":"2026-09-19T12:12:01+10:00"}
        """;
        var (updater, _) = UpdaterFor(HttpStatusCode.OK, body);

        var release = await updater.GetLatestAsync(Addon, "pre-release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("0.5.0-pre-release.e73f4bf", release.Version);
        Assert.Equal("HoobiScripts-0.5.0-pre-release.e73f4bf.zip", release.Zip);
        Assert.Equal("fad9149904b658d1f066f4f41c9301f8f18070249c44b5a3480bb558aefd0983", release.Sha256);
        Assert.Equal(26222, release.Size);
        Assert.Equal(2026, release.Released.Year);
    }

    [Fact]
    public async Task GetLatestAsync_RequestsTheChannelManifestBesideTheBaseUrl()
    {
        var (updater, handler) = UpdaterFor(HttpStatusCode.OK, "null");

        await updater.GetLatestAsync(Addon, "release", CancellationToken.None);

        Assert.Equal("https://addon.hoobi.io/hoobiscripts/latest-release.json", handler.LastUri?.ToString());
    }

    [Fact]
    public async Task GetLatestAsync_NonSuccessStatus_Throws()
    {
        var (updater, _) = UpdaterFor(HttpStatusCode.NotFound, "not found");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.GetLatestAsync(Addon, "pre-release", CancellationToken.None));
    }

    [Fact]
    public async Task ProbeChannelsAsync_MissingManifest_IsNullNotAnError()
    {
        const string releaseBody = """
        {"version":"1.0.0","channel":"release","zip":"HoobiScripts-1.0.0.zip",
         "sha256":"abc123","size":100,"released":"2026-09-19T12:12:01+10:00"}
        """;
        var updater = UpdaterFor(uri => uri.ToString().EndsWith("latest-release.json", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, releaseBody)
            : uri.ToString().EndsWith("latest-pre-release.json", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, "null")
                : (HttpStatusCode.NotFound, "not found"));

        var releases = await updater.ProbeChannelsAsync(Addon, ["release", "pre-release", "beta"], CancellationToken.None);

        Assert.NotNull(releases["release"]);
        Assert.Null(releases["pre-release"]);
        Assert.Null(releases["beta"]);
    }

    [Fact]
    public async Task ProbeChannelsAsync_ServerError_Propagates()
    {
        var updater = UpdaterFor(uri => uri.ToString().EndsWith("latest-pre-release.json", StringComparison.Ordinal)
            ? (HttpStatusCode.InternalServerError, "boom")
            : (HttpStatusCode.OK, "null"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.ProbeChannelsAsync(Addon, ["release", "pre-release"], CancellationToken.None));
    }

    [Fact]
    public async Task ProbeChannelsAsync_ReturnsOneEntryPerRequestedChannel()
    {
        var updater = UpdaterFor(_ => (HttpStatusCode.OK, "null"));

        var releases = await updater.ProbeChannelsAsync(Addon, ["release", "pre-release"], CancellationToken.None);

        Assert.Equal(["pre-release", "release"], releases.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Features_NotConfigured_DefaultsToAddonsOnly()
    {
        Assert.Equal(["addons"], Addon.Features);
    }

    [Fact]
    public void Features_Configured_UsesThemVerbatim()
    {
        var addon = new ManagedAddon("restedxp", "RXPGuides", "https://addon.example/restedxp/", Features: ["addons", "guides"]);

        Assert.Equal(["addons", "guides"], addon.Features);
    }
}
