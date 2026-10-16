using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class ReleaseUpdateTests
{
    private const string Deb = "-linux-x64.deb";

    private static readonly byte[] DebBytes = "deb package"u8.ToArray();

    private static readonly string DebSha = Convert.ToHexStringLower(SHA256.HashData(DebBytes));

    private static string Url(string tag, string file) => $"https://github.com/hoobio/steward-companion/releases/download/{tag}/{file}";

    private static object Release(string tag, bool prerelease, string published, bool draft = false, params string[] assets) => new
    {
        tag_name = tag,
        draft,
        prerelease,
        published_at = published,
        html_url = $"https://github.com/hoobio/steward-companion/releases/tag/{tag}",
        assets = assets.Select(name => new { name, browser_download_url = Url(tag, name), size = DebBytes.Length, digest = $"sha256:{DebSha}" }),
    };

    private static string Json(params object[] releases) => JsonSerializer.Serialize(releases);

    private static string Preview(int run) => $"Steward-0.23.2-preview.{run}";

    [Fact]
    public void LatestRelease_PicksTheHighestPreviewByNumberNotListOrder()
    {
        var json = Json(
            Release("desktop-preview-9", true, "2026-10-16T09:00:00Z", false, Preview(9) + Deb),
            Release("desktop-preview-23", true, "2026-10-15T09:00:00Z", false, Preview(23) + Deb),
            Release("desktop-preview-10", true, "2026-10-14T09:00:00Z", false, Preview(10) + Deb));

        var latest = AppUpdater.LatestRelease(json, Deb, includePreRelease: true);

        Assert.Equal("0.23.2-preview.23", latest!.Version);
        Assert.Equal(Url("desktop-preview-23", Preview(23) + Deb), latest.Zip);
        Assert.Equal(DebSha, latest.Sha256);
        Assert.Equal("https://github.com/hoobio/steward-companion/releases/tag/desktop-preview-23", latest.Website);
    }

    [Fact]
    public void LatestRelease_SkipsReleasesWithoutThisPlatformsPackage()
    {
        var json = Json(
            Release("v0.24.0", false, "2026-10-17T09:00:00Z", false, "Steward-0.24.0-x64.msi"),
            Release("desktop-preview-31", true, "2026-10-16T09:00:00Z", false, Preview(31) + "-linux-x64.rpm", Preview(31) + "-linux-arm64.deb", Preview(31) + Deb));

        Assert.Equal("0.23.2-preview.31", AppUpdater.LatestRelease(json, Deb, includePreRelease: true)!.Version);
        Assert.Equal(Url("desktop-preview-31", Preview(31) + "-linux-x64.rpm"), AppUpdater.LatestRelease(json, "-linux-x64.rpm", includePreRelease: true)!.Zip);
    }

    [Fact]
    public void LatestRelease_LeavesPreReleasesOutOfAStableBuildAndDraftsOutOfEvery()
    {
        var json = Json(
            Release("desktop-preview-31", true, "2026-10-16T09:00:00Z", false, Preview(31) + Deb),
            Release("v0.24.0", false, "2026-10-17T09:00:00Z", true, "Steward-0.24.0" + Deb),
            Release("v0.23.2", false, "2026-10-14T09:00:00Z", false, "Steward-0.23.2" + Deb));

        Assert.Equal("0.23.2", AppUpdater.LatestRelease(json, Deb, includePreRelease: false)!.Version);
        Assert.Equal("0.23.2", AppUpdater.LatestRelease(json, Deb, includePreRelease: true)!.Version);
    }

    [Fact]
    public void LatestRelease_AnAssetWithoutADigestOrAVersion_IsSkipped()
    {
        var json = $$"""
            [{"tag_name":"desktop-preview-40","prerelease":true,"assets":[
              {"name":"{{Preview(40)}}{{Deb}}","browser_download_url":"{{Url("desktop-preview-40", Preview(40) + Deb)}}","digest":null},
              {"name":"Steward-latest{{Deb}}","browser_download_url":"{{Url("desktop-preview-40", "Steward-latest" + Deb)}}","digest":"sha256:{{DebSha}}"}]}]
            """;

        Assert.Null(AppUpdater.LatestRelease(json, Deb, includePreRelease: true));
    }

    [Fact]
    public void LatestRelease_NotAnArray_Throws() =>
        Assert.Throws<JsonException>(() => AppUpdater.LatestRelease("""{"message":"API rate limit exceeded"}""", Deb, true));

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(route(request));
        }
    }

    private static HttpResponseMessage Ok(string body, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        return response;
    }

    private static AppUpdater Updater(Stub stub) =>
        new(new HttpClient(stub), new Uri("https://api.example.com/latest-release.json"), "9ABC");

    [Fact]
    public async Task CheckReleaseAsync_OffersTheNewerPreviewAndRevalidatesWithTheETag()
    {
        var json = Json(Release("desktop-preview-31", true, "2026-10-16T09:00:00Z", false, Preview(31) + Deb));
        var stub = new Stub(request => request.Headers.IfNoneMatch.Count > 0 ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok(json, "\"abc\""));
        var updater = Updater(stub);

        var first = await updater.CheckReleaseAsync("0.23.2-preview.28", Deb, TestContext.Current.CancellationToken);
        var second = await updater.CheckReleaseAsync("0.23.2-preview.28", Deb, TestContext.Current.CancellationToken);

        Assert.Equal("0.23.2-preview.31", first!.Version);
        Assert.Equal(first, second);
        Assert.Equal("https://api.github.com/repos/hoobio/steward-companion/releases?per_page=30", stub.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains(stub.Requests[0].Headers.Accept, accept => accept.MediaType == "application/vnd.github+json");
        Assert.NotEmpty(stub.Requests[0].Headers.UserAgent);
        Assert.Equal("\"abc\"", stub.Requests[1].Headers.IfNoneMatch.Single().Tag);
    }

    [Theory]
    [InlineData("0.23.2-preview.31")]
    [InlineData("0.23.2-preview.40")]
    [InlineData("0.24.0")]
    public async Task CheckReleaseAsync_NothingNewer_IsNull(string installed)
    {
        var json = Json(Release("desktop-preview-31", true, "2026-10-16T09:00:00Z", false, Preview(31) + Deb));

        Assert.Null(await Updater(new Stub(_ => Ok(json))).CheckReleaseAsync(installed, Deb, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckReleaseAsync_RateLimited_Throws()
    {
        var stub = new Stub(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });

        await Assert.ThrowsAsync<HttpRequestException>(() => Updater(stub).CheckReleaseAsync("0.23.2-preview.28", Deb, TestContext.Current.CancellationToken));
    }

    private static Stub DownloadStub(string releaseJson) => new(request => request.RequestUri!.Host == "api.github.com"
        ? Ok(releaseJson)
        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(DebBytes) });

    private static string TagJson(string tag, string file, string? digest = null) => JsonSerializer.Serialize(new
    {
        tag_name = tag,
        assets = new[] { new { name = file, browser_download_url = Url(tag, file), digest = digest ?? $"sha256:{DebSha}" } },
    });

    private static AddonRelease Offered(string version, string tag, string file, string? sha = null) =>
        new(version, Url(tag, file), sha ?? DebSha, DebBytes.Length, DateTimeOffset.UtcNow);

    [Fact]
    public async Task DownloadReleaseAssetAsync_VerifiedPreviewPackage_LandsInTheGivenFolder()
    {
        var file = Preview(31) + Deb;
        var stub = DownloadStub(TagJson("desktop-preview-31", file));
        var directory = Directory.CreateTempSubdirectory("steward-test-");
        try
        {
            var path = await Updater(stub).DownloadReleaseAssetAsync(
                Offered("0.23.2-preview.31", "desktop-preview-31", file), ".deb", directory.FullName, null, TestContext.Current.CancellationToken);

            Assert.Equal(Path.Combine(directory.FullName, file), path);
            Assert.Equal(DebBytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal("https://api.github.com/repos/hoobio/steward-companion/releases/tags/desktop-preview-31", stub.Requests[0].RequestUri!.AbsoluteUri);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("0.23.2-preview.31", "desktop-preview-29", "Steward-0.23.2-preview.31-linux-x64.deb")]
    [InlineData("0.23.2-preview.31", "desktop-preview-31", "Steward-0.23.2-preview.29-linux-x64.deb")]
    [InlineData("0.23.2-preview.31", "desktop-preview-31", "Steward-0.23.2-preview.31-linux-x64.rpm")]
    [InlineData("0.23.2-preview.31", "desktop-preview-x", "Steward-0.23.2-preview.31-linux-x64.deb")]
    [InlineData("0.24.0", "v0.23.2", "Steward-0.24.0-linux-x64.deb")]
    public async Task DownloadReleaseAssetAsync_PackageNotFromThatVersionsRelease_IsRefusedBeforeAnyRequest(string version, string tag, string file)
    {
        var stub = DownloadStub(TagJson(tag, file));

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Updater(stub).DownloadReleaseAssetAsync(
            Offered(version, tag, file), ".deb", Path.GetTempPath(), null, TestContext.Current.CancellationToken));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task DownloadReleaseAssetAsync_UrlOffGitHub_IsRefusedBeforeAnyRequest()
    {
        var stub = DownloadStub(TagJson("desktop-preview-31", Preview(31) + Deb));
        var release = Offered("0.23.2-preview.31", "desktop-preview-31", Preview(31) + Deb) with
        {
            Zip = $"https://evil.example/hoobio/steward-companion/releases/download/desktop-preview-31/{Preview(31)}{Deb}",
        };

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Updater(stub).DownloadReleaseAssetAsync(
            release, ".deb", Path.GetTempPath(), null, TestContext.Current.CancellationToken));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task DownloadReleaseAssetAsync_DigestDifferentFromGitHub_IsRefusedWithoutDownloading()
    {
        var file = Preview(31) + Deb;
        var stub = DownloadStub(TagJson("desktop-preview-31", file, $"sha256:{new string('0', 64)}"));

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Updater(stub).DownloadReleaseAssetAsync(
            Offered("0.23.2-preview.31", "desktop-preview-31", file), ".deb", Path.GetTempPath(), null, TestContext.Current.CancellationToken));
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData("v0.23.2", "0.23.2", false, true)]
    [InlineData("desktop-preview-31", "0.23.2-preview.31", true, true)]
    [InlineData("desktop-preview-31", "0.23.2-preview.31", false, false)]
    [InlineData("desktop-preview-31", "0.23.2-preview.131", true, false)]
    [InlineData("desktop-preview-", "0.23.2-preview.", true, false)]
    public void TagMatches_BindsThePackageToItsRelease(string tag, string version, bool allowPreviewTag, bool expected) =>
        Assert.Equal(expected, AppUpdater.TagMatches(tag, version, allowPreviewTag));
}
