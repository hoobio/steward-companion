using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class AppUpdaterTests
{
    public AppUpdaterTests() => GitHubReleases.ResetCache();

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

    private static string Entry(string tag, string publishedAt, bool prerelease, params string[] assetNames)
    {
        var assets = string.Join(',', assetNames.Select(name =>
            $$"""{"name":"{{name}}","size":100,"digest":"sha256:abc123","browser_download_url":"https://github.com/hoobio/steward-companion/releases/download/{{tag}}/{{name}}"}"""));
        return $$"""{"tag_name":"{{tag}}","published_at":"{{publishedAt}}","prerelease":{{(prerelease ? "true" : "false")}},"assets":[{{assets}}]}""";
    }

    private static string ReleaseBody(string tag, params string[] assetNames) =>
        $"[{Entry(tag, "2026-09-19T16:18:31Z", prerelease: false, assetNames)}]";

    private static string Body(params string[] entries) => $"[{string.Join(',', entries)}]";

    [Fact]
    public async Task CheckAsync_NewerTag_ReturnsRelease()
    {
        var updater = UpdaterFor(ReleaseBody("v0.4.0", "Steward-0.4.0.msi"));

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), "0.3.0", "release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("v0.4.0", release.Version);
        Assert.Equal("https://github.com/hoobio/steward-companion/releases/download/v0.4.0/Steward-0.4.0.msi", release.Zip);
    }

    [Fact]
    public async Task CheckAsync_SameVersionWithFourPartCurrent_ReturnsNull()
    {
        var updater = UpdaterFor(ReleaseBody("v0.3.0", "Steward-0.3.0.msi"));

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), "0.3.0", "release", CancellationToken.None);

        Assert.Null(release);
    }

    [Theory]
    [InlineData(0, 8, 0, "0.8.0-pre-release.83.e58ca45")]
    [InlineData(0, 8, 1, "0.8.1-pre-release.88.466c7c3")]
    public async Task CheckAsync_ReleaseChannel_OnPreReleaseBuild_ReturnsTheLatestRelease(int major, int minor, int build, string installedVersion)
    {
        var updater = UpdaterFor(ReleaseBody("v0.8.0", "Steward-0.8.0-x64.msi"));

        var release = await updater.CheckAsync(new Version(major, minor, build, 0), installedVersion, "release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("v0.8.0", release.Version);
    }

    [Fact]
    public async Task CheckAsync_NoMsiAsset_ReturnsNull()
    {
        var updater = UpdaterFor(ReleaseBody("v0.4.0", "Steward-0.4.0.zip"));

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), "0.3.0", "release", CancellationToken.None);

        Assert.Null(release);
    }

    private const string PreReleaseTag = "v0.7.2-pre-release.abc1234";

    private static string PreReleaseNewerThanRelease() => Body(
        Entry(PreReleaseTag, "2026-09-20T10:00:00Z", prerelease: true, "Steward-0.7.2.msi"),
        Entry("v0.7.2", "2026-09-19T10:00:00Z", prerelease: false, "Steward-0.7.2.msi"));

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_NewerPreRelease_ReturnsPreRelease()
    {
        var updater = UpdaterFor(PreReleaseNewerThanRelease());

        var release = await updater.CheckAsync(new Version(0, 7, 2), "0.7.2", "pre-release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal(PreReleaseTag, release.Version);
    }

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_AlreadyOnThatPreRelease_ReturnsNull()
    {
        var updater = UpdaterFor(PreReleaseNewerThanRelease());

        var release = await updater.CheckAsync(new Version(0, 7, 2), "0.7.2-pre-release.abc1234", "pre-release", CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_NewerRelease_ReturnsRelease()
    {
        var updater = UpdaterFor(Body(
            Entry("v0.7.3", "2026-09-21T10:00:00Z", prerelease: false, "Steward-0.7.3.msi"),
            Entry(PreReleaseTag, "2026-09-20T10:00:00Z", prerelease: true, "Steward-0.7.2.msi")));

        var release = await updater.CheckAsync(new Version(0, 7, 2), "0.7.2-pre-release.abc1234", "pre-release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("v0.7.3", release.Version);
    }

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_LowerNumericPreRelease_ReturnsNull()
    {
        var updater = UpdaterFor(Body(
            Entry("v0.7.1-pre-release.abc1234", "2026-09-21T10:00:00Z", prerelease: true, "Steward-0.7.1.msi"),
            Entry("v0.7.2", "2026-09-19T10:00:00Z", prerelease: false, "Steward-0.7.2.msi")));

        var release = await updater.CheckAsync(new Version(0, 7, 2), "0.7.2", "pre-release", CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task CheckAsync_ReleaseChannel_IgnoresNewerPreRelease()
    {
        var updater = UpdaterFor(PreReleaseNewerThanRelease());

        var release = await updater.CheckAsync(new Version(0, 7, 2), "0.7.2", "release", CancellationToken.None);

        Assert.Null(release);
    }
}
