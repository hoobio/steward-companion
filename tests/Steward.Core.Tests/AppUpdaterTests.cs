using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class AppUpdaterTests
{
    private const string BaseUrl = "https://addon.hoobi.io/steward-companion/";

    private sealed class ManifestHandler(IReadOnlyDictionary<string, string> manifests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.Segments[^1];
            return Task.FromResult(manifests.TryGetValue(name, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) });
        }
    }

    private static string Manifest(string version, string releasedAt) =>
        $$"""
        {"version":"{{version}}","channel":"release","zip":"https://github.com/hoobio/steward-companion/releases/download/v{{version}}/Steward-{{version}}-x64.msi","sha256":"abc123","size":100,"released":"{{releasedAt}}"}
        """;

    private static AppUpdater UpdaterFor(string? release = null, string? preRelease = null)
    {
        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        if (release is not null)
        {
            manifests["latest-release.json"] = release;
        }

        if (preRelease is not null)
        {
            manifests["latest-pre-release.json"] = preRelease;
        }

        return new AppUpdater(new HttpClient(new ManifestHandler(manifests)), BaseUrl);
    }

    [Fact]
    public async Task CheckAsync_NewerVersion_ReturnsRelease()
    {
        var updater = UpdaterFor(release: Manifest("0.4.0", "2026-09-19T16:18:31Z"));

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), "0.3.0", "release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("0.4.0", release.Version);
        Assert.Equal("https://github.com/hoobio/steward-companion/releases/download/v0.4.0/Steward-0.4.0-x64.msi", release.Zip);
    }

    [Fact]
    public async Task CheckAsync_SameVersionWithFourPartCurrent_ReturnsNull()
    {
        var updater = UpdaterFor(release: Manifest("0.3.0", "2026-09-19T16:18:31Z"));

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), "0.3.0", "release", CancellationToken.None);

        Assert.Null(release);
    }

    [Theory]
    [InlineData(0, 8, 0, "0.8.0-pre-release.83.e58ca45")]
    [InlineData(0, 8, 1, "0.8.1-pre-release.88.466c7c3")]
    public async Task CheckAsync_ReleaseChannel_OnPreReleaseBuild_ReturnsTheLatestRelease(int major, int minor, int build, string installedVersion)
    {
        var updater = UpdaterFor(release: Manifest("0.8.0", "2026-09-20T09:52:17Z"));

        var release = await updater.CheckAsync(new Version(major, minor, build, 0), installedVersion, "release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("0.8.0", release.Version);
    }

    [Fact]
    public async Task CheckAsync_NoManifestOnTheChannel_ReturnsNull()
    {
        var updater = UpdaterFor();

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), "0.3.0", "release", CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task CheckAsync_EmptyChannelManifest_ReturnsNull()
    {
        var updater = UpdaterFor(release: "null");

        var release = await updater.CheckAsync(new Version(0, 3, 0, 0), "0.3.0", "release", CancellationToken.None);

        Assert.Null(release);
    }

    private const string PreReleaseVersion = "0.7.2-pre-release.abc1234";

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_NewerPreRelease_ReturnsPreRelease()
    {
        var updater = UpdaterFor(
            release: Manifest("0.7.2", "2026-09-19T10:00:00Z"),
            preRelease: Manifest(PreReleaseVersion, "2026-09-20T10:00:00Z"));

        var release = await updater.CheckAsync(new Version(0, 7, 2), "0.7.2", "pre-release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal(PreReleaseVersion, release.Version);
    }

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_AlreadyOnThatPreRelease_ReturnsNull()
    {
        var updater = UpdaterFor(
            release: Manifest("0.7.2", "2026-09-19T10:00:00Z"),
            preRelease: Manifest(PreReleaseVersion, "2026-09-20T10:00:00Z"));

        var release = await updater.CheckAsync(new Version(0, 7, 2), PreReleaseVersion, "pre-release", CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_NewerRelease_ReturnsRelease()
    {
        var updater = UpdaterFor(
            release: Manifest("0.7.3", "2026-09-21T10:00:00Z"),
            preRelease: Manifest(PreReleaseVersion, "2026-09-20T10:00:00Z"));

        var release = await updater.CheckAsync(new Version(0, 7, 2), PreReleaseVersion, "pre-release", CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("0.7.3", release.Version);
    }

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_LowerNumericPreRelease_ReturnsNull()
    {
        var updater = UpdaterFor(
            release: Manifest("0.7.2", "2026-09-19T10:00:00Z"),
            preRelease: Manifest("0.7.1-pre-release.abc1234", "2026-09-21T10:00:00Z"));

        var release = await updater.CheckAsync(new Version(0, 7, 2), "0.7.2", "pre-release", CancellationToken.None);

        Assert.Null(release);
    }

    [Fact]
    public async Task CheckAsync_ReleaseChannel_IgnoresNewerPreRelease()
    {
        var updater = UpdaterFor(
            release: Manifest("0.7.2", "2026-09-19T10:00:00Z"),
            preRelease: Manifest(PreReleaseVersion, "2026-09-20T10:00:00Z"));

        var release = await updater.CheckAsync(new Version(0, 7, 2), "0.7.2", "release", CancellationToken.None);

        Assert.Null(release);
    }
}
