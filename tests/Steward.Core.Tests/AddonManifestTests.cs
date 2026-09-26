using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class AddonManifestTests
{
    public AddonManifestTests() => GitHubReleases.ResetCache();

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
        {"version":"0.5.0-develop.e73f4bf","channel":"develop","zip":"HoobiScripts-0.5.0-develop.e73f4bf.zip",
         "sha256":"fad9149904b658d1f066f4f41c9301f8f18070249c44b5a3480bb558aefd0983","size":26222,
         "released":"2026-09-19T12:12:01+10:00"}
        """;
        var (updater, _) = UpdaterFor(HttpStatusCode.OK, body);

        var release = await updater.GetLatestAsync(Addon, "develop", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("0.5.0-develop.e73f4bf", release.Version);
        Assert.Equal("HoobiScripts-0.5.0-develop.e73f4bf.zip", release.Zip);
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

        var releases = await updater.ProbeChannelsAsync(Addon, ["release", "pre-release", "develop"], CancellationToken.None);

        Assert.NotNull(releases["release"]);
        Assert.Null(releases["pre-release"]);
        Assert.Null(releases["develop"]);
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

    private static readonly ManagedAddon GitHubAddon =
        new("restedxp", "RXPGuides", GitHubRepo: "RestedXP/RXPGuides");

    [Fact]
    public async Task GetLatestAsync_GitHubAddon_MapsTheLatestReleaseZipAsset()
    {
        const string body = """
        [{"tag_name":"v4.11.4","prerelease":false,"draft":false,"published_at":"2026-09-19T16:18:31Z","assets":[{"name":"release.json","size":397,"digest":"sha256:a10b","browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/release.json"},{"name":"RXPGuides-v4.11.4.zip","size":8165822,"digest":"sha256:a9a05db2","browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/RXPGuides-v4.11.4.zip"}]}]
        """;
        var (updater, handler) = UpdaterFor(HttpStatusCode.OK, body);

        var release = await updater.GetLatestAsync(GitHubAddon, "release", CancellationToken.None);

        Assert.Equal("https://api.github.com/repos/RestedXP/RXPGuides/releases?per_page=20", handler.LastUri?.ToString());
        Assert.NotEmpty(handler.LastRequest!.Headers.UserAgent);
        Assert.NotNull(release);
        Assert.Equal("v4.11.4", release.Version);
        Assert.Equal("https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/RXPGuides-v4.11.4.zip", release.Zip);
        Assert.Equal("a9a05db2", release.Sha256);
        Assert.Equal(8165822, release.Size);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 16, 18, 31, TimeSpan.Zero), release.Released);
    }

    [Fact]
    public async Task GetLatestAsync_GitHubAddon_PrereleaseChannel_TakesNewestNonDraftPrerelease()
    {
        const string body = """
        [
          {"tag_name":"v4.12.0-draft","prerelease":true,"draft":true,"published_at":"2026-09-19T18:00:00Z","assets":[]},
          {"tag_name":"v4.12.0-rc1","prerelease":true,"draft":false,"published_at":"2026-09-19T17:00:00Z","assets":[{"name":"RXPGuides-v4.12.0-rc1.zip","size":100,"digest":"sha256:abc","browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.12.0-rc1/RXPGuides-v4.12.0-rc1.zip"}]},
          {"tag_name":"v4.11.4","prerelease":false,"draft":false,"published_at":"2026-09-19T16:18:31Z","assets":[{"name":"RXPGuides-v4.11.4.zip","size":100,"digest":"sha256:def","browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/RXPGuides-v4.11.4.zip"}]}
        ]
        """;
        var (updater, handler) = UpdaterFor(HttpStatusCode.OK, body);

        var release = await updater.GetLatestAsync(GitHubAddon, "pre-release", CancellationToken.None);

        Assert.Equal("https://api.github.com/repos/RestedXP/RXPGuides/releases?per_page=20", handler.LastUri?.ToString());
        Assert.NotNull(release);
        Assert.Equal("v4.12.0-rc1", release.Version);
    }

    [Fact]
    public async Task GetLatestAsync_GitHubAddon_BothChannelsUseOneRequest()
    {
        const string body = """
        [
          {"tag_name":"v4.12.0-rc1","prerelease":true,"draft":false,"published_at":"2026-09-19T17:00:00Z","assets":[{"name":"RXPGuides-v4.12.0-rc1.zip","size":100,"digest":"sha256:abc","browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.12.0-rc1/RXPGuides-v4.12.0-rc1.zip"}]},
          {"tag_name":"v4.11.4","prerelease":false,"draft":false,"published_at":"2026-09-19T16:18:31Z","assets":[{"name":"RXPGuides-v4.11.4.zip","size":100,"digest":"sha256:def","browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/RXPGuides-v4.11.4.zip"}]}
        ]
        """;
        var (updater, handler) = UpdaterFor(HttpStatusCode.OK, body);

        var release = await updater.GetLatestAsync(GitHubAddon, "release", CancellationToken.None);
        var preRelease = await updater.GetLatestAsync(GitHubAddon, "pre-release", CancellationToken.None);

        Assert.Equal("v4.11.4", release?.Version);
        Assert.Equal("v4.12.0-rc1", preRelease?.Version);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetLatestAsync_GitHubAddon_UnknownChannel_IsNullWithoutARequest()
    {
        var (updater, handler) = UpdaterFor(HttpStatusCode.OK, "null");

        var release = await updater.GetLatestAsync(GitHubAddon, "develop", CancellationToken.None);

        Assert.Null(release);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task GetLatestAsync_GitHubAddon_MissingDigest_Throws()
    {
        const string body = """
        [{"tag_name":"v4.11.4","prerelease":false,"draft":false,"published_at":"2026-09-19T16:18:31Z","assets":[{"name":"RXPGuides-v4.11.4.zip","size":8165822,"browser_download_url":"https://github.com/RestedXP/RXPGuides/releases/download/v4.11.4/RXPGuides-v4.11.4.zip"}]}]
        """;
        var (updater, _) = UpdaterFor(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.GetLatestAsync(GitHubAddon, "release", CancellationToken.None));
    }

    [Fact]
    public void Features_NotConfigured_DefaultsToAddonsOnly()
    {
        Assert.Equal(["addons"], Addon.Features);
    }

    [Fact]
    public void Features_Configured_UsesThemVerbatim()
    {
        var addon = new ManagedAddon("restedxp", "RXPGuides", GitHubRepo: "RestedXP/RXPGuides", Features: ["addons", "guides"]);

        Assert.Equal(["addons", "guides"], addon.Features);
    }
}
