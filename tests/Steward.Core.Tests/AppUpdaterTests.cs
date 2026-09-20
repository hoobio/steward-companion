using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class AppUpdaterTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static AppUpdater UpdaterFor(string body) =>
        new(new HttpClient(new StubHandler(HttpStatusCode.OK, body)), "hoobio/steward-companion");

    private static string ReleaseBody(string tag, params string[] assetNames)
    {
        var assets = string.Join(',', assetNames.Select(name =>
            $$"""{"name":"{{name}}","size":100,"digest":"sha256:abc123","browser_download_url":"https://github.com/hoobio/steward-companion/releases/download/{{tag}}/{{name}}"}"""));
        return $$"""{"tag_name":"{{tag}}","published_at":"2026-09-19T16:18:31Z","assets":[{{assets}}]}""";
    }

    [Fact]
    public async Task CheckAsync_NewerTag_ReturnsRelease()
    {
        var updater = UpdaterFor(ReleaseBody("v0.4.0", "Steward-0.4.0.msi"));

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("v0.4.0", release.Version);
        Assert.Equal("https://github.com/hoobio/steward-companion/releases/download/v0.4.0/Steward-0.4.0.msi", release.Zip);
    }

    [Fact]
    public async Task CheckAsync_SameVersionWithFourPartCurrent_ReturnsNull()
    {
        var updater = UpdaterFor(ReleaseBody("v0.3.0", "Steward-0.3.0.msi"));

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task CheckAsync_NoMsiAsset_ReturnsNull()
    {
        var updater = UpdaterFor(ReleaseBody("v0.4.0", "Steward-0.4.0.zip"));

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), CancellationToken.None);

        Assert.Null(release);
    }
}
