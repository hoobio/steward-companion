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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastRequest = request;
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

        var release = await updater.GetLatestAsync(Addon, "beta", CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task GetLatestAsync_PopulatedChannel_DeserialisesTheRelease()
    {
        const string body = """
        {"version":"0.5.0-unstable.e73f4bf","channel":"unstable","zip":"HoobiScripts-0.5.0-unstable.e73f4bf.zip",
         "sha256":"fad9149904b658d1f066f4f41c9301f8f18070249c44b5a3480bb558aefd0983","size":26222,
         "released":"2026-09-19T12:12:01+10:00"}
        """;
        var (updater, _) = UpdaterFor(HttpStatusCode.OK, body);

        var release = await updater.GetLatestAsync(Addon, "unstable", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("0.5.0-unstable.e73f4bf", release.Version);
        Assert.Equal("HoobiScripts-0.5.0-unstable.e73f4bf.zip", release.Zip);
        Assert.Equal("fad9149904b658d1f066f4f41c9301f8f18070249c44b5a3480bb558aefd0983", release.Sha256);
        Assert.Equal(26222, release.Size);
        Assert.Equal(2026, release.Released.Year);
    }

    [Fact]
    public async Task GetLatestAsync_RequestsTheChannelManifestBesideTheBaseUrl()
    {
        var (updater, handler) = UpdaterFor(HttpStatusCode.OK, "null");

        await updater.GetLatestAsync(Addon, "stable", CancellationToken.None);

        Assert.Equal("https://addon.hoobi.io/hoobiscripts/latest-stable.json", handler.LastUri?.ToString());
    }

    [Fact]
    public async Task GetLatestAsync_NonSuccessStatus_Throws()
    {
        var (updater, _) = UpdaterFor(HttpStatusCode.NotFound, "not found");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.GetLatestAsync(Addon, "beta", CancellationToken.None));
    }

    [Fact]
    public async Task ProbeChannelsAsync_MissingManifest_IsNullNotAnError()
    {
        const string releaseBody = """
        {"version":"1.0.0","channel":"stable","zip":"HoobiScripts-1.0.0.zip",
         "sha256":"abc123","size":100,"released":"2026-09-19T12:12:01+10:00"}
        """;
        var updater = UpdaterFor(uri => uri.ToString().EndsWith("latest-stable.json", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, releaseBody)
            : uri.ToString().EndsWith("latest-beta.json", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, "null")
                : (HttpStatusCode.NotFound, "not found"));

        var releases = await updater.ProbeChannelsAsync(Addon, ["stable", "beta", "unstable"], CancellationToken.None);

        Assert.NotNull(releases["stable"]);
        Assert.Null(releases["beta"]);
        Assert.Null(releases["unstable"]);
    }

    [Fact]
    public async Task ProbeChannelsAsync_ServerError_Propagates()
    {
        var updater = UpdaterFor(uri => uri.ToString().EndsWith("latest-beta.json", StringComparison.Ordinal)
            ? (HttpStatusCode.InternalServerError, "boom")
            : (HttpStatusCode.OK, "null"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.ProbeChannelsAsync(Addon, ["stable", "beta"], CancellationToken.None));
    }

    [Fact]
    public async Task ProbeChannelsAsync_ReturnsOneEntryPerRequestedChannel()
    {
        var updater = UpdaterFor(_ => (HttpStatusCode.OK, "null"));

        var releases = await updater.ProbeChannelsAsync(Addon, ["stable", "beta"], CancellationToken.None);

        Assert.Equal(["beta", "stable"], releases.Keys.OrderBy(k => k));
    }

    private static readonly ManagedAddon GitHubAddon =
        new("restedxp", "RXPGuides", "https://addon.hoobi.io/restedxp/", GitHubRepo: "RestedXP/RXPGuides");

    [Fact]
    public async Task GetLatestAsync_GitHubAddon_MapsTheLatestReleaseZipAsset()
    {
        const string body = """
        {"tag_name":"v4.11.4","prerelease":false,"draft":false,"published_at":"2026-09-19T16:18:31Z","assets":[{"name":"release.json","size":397,"digest":"sha256:a10b","browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/release.json"},{"name":"RXPGuides-v4.11.4.zip","size":8165822,"digest":"sha256:a9a05db2","browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/RXPGuides-v4.11.4.zip"}]}
        """;
        var (updater, handler) = UpdaterFor(HttpStatusCode.OK, body);

        var release = await updater.GetLatestAsync(GitHubAddon, "stable", CancellationToken.None);

        Assert.Equal("https://api.github.com/repos/RestedXP/RXPGuides/releases/latest", handler.LastUri?.ToString());
        Assert.NotEmpty(handler.LastRequest!.Headers.UserAgent);
        Assert.NotNull(release);
        Assert.Equal("v4.11.4", release.Version);
        Assert.Equal("https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/RXPGuides-v4.11.4.zip", release.Zip);
        Assert.Equal("a9a05db2", release.Sha256);
        Assert.Equal(8165822, release.Size);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 16, 18, 31, TimeSpan.Zero), release.Released);
    }

    [Fact]
    public async Task GetLatestAsync_GitHubAddon_NonStableChannel_IsNullWithoutARequest()
    {
        var (updater, handler) = UpdaterFor(HttpStatusCode.OK, "null");

        var release = await updater.GetLatestAsync(GitHubAddon, "beta", CancellationToken.None);

        Assert.Null(release);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task GetLatestAsync_GitHubAddon_MissingDigest_Throws()
    {
        const string body = """
        {"tag_name":"v4.11.4","prerelease":false,"draft":false,"published_at":"2026-09-19T16:18:31Z","assets":[{"name":"RXPGuides-v4.11.4.zip","size":8165822,"browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/RXPGuides-v4.11.4.zip"}]}
        """;
        var (updater, _) = UpdaterFor(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.GetLatestAsync(GitHubAddon, "stable", CancellationToken.None));
    }
}
