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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
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
}
