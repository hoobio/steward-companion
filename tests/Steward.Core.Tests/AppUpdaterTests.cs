using System.Net;

namespace Steward.Core.Tests;

public sealed class AppUpdaterTests
{
    [Theory]
    [InlineData("v0.20.2", "0.20.1", true)]
    [InlineData("0.21.0", "0.20.9", true)]
    [InlineData("v1.0.0", "0.99.99", true)]
    [InlineData("v0.20.2", "0.20.2", false)]
    [InlineData("0.20.2", "0.20.2.0", false)]
    [InlineData("v0.20.1", "0.20.2", false)]
    [InlineData("v0.10.0", "0.9.0", true)]
    [InlineData("V0.20.3", "0.20.2", true)]
    [InlineData("not-a-version", "0.20.2", false)]
    [InlineData("v0.20.3", "unknown", false)]
    [InlineData("", "0.20.2", false)]
    public void IsNewer_ComparesNumerically(string available, string installed, bool expected) =>
        Assert.Equal(expected, AppUpdater.IsNewer(available, installed));

    [Fact]
    public void InstallMsiScript_InstallsQuietlyWithAVerboseLog()
    {
        var script = AppUpdater.InstallMsiScript(@"C:\Temp\Steward-0.20.2.msi", @"C:\Logs\update.log");

        Assert.Contains(@"Start-Process msiexec.exe -ArgumentList '/i', '""C:\Temp\Steward-0.20.2.msi""', '/qn', '/l*v', '""C:\Logs\update.log""' -PassThru -Wait", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallMsiScript_StopsBeforeDeletingTheMsiOnAFailedInstall()
    {
        var script = AppUpdater.InstallMsiScript(@"C:\Temp\Steward-0.20.2.msi", @"C:\Logs\update.log");

        var install = script.IndexOf("msiexec.exe", StringComparison.Ordinal);
        var guard = script.IndexOf("if ($exitCode -ne 0 -and $exitCode -ne 3010) { exit $exitCode }", StringComparison.Ordinal);
        var delete = script.IndexOf(@"Remove-Item -LiteralPath 'C:\Temp\Steward-0.20.2.msi'", StringComparison.Ordinal);
        Assert.True(install >= 0 && install < guard && guard < delete, script);
    }

    [Fact]
    public void InstallMsiScript_EscapesSingleQuotes()
    {
        var script = AppUpdater.InstallMsiScript(@"C:\O'Brien\Steward-0.20.2.msi", @"C:\O'Brien\update.log");

        Assert.Contains(@"'""C:\O''Brien\Steward-0.20.2.msi""'", script, StringComparison.Ordinal);
        Assert.Contains(@"'""C:\O''Brien\update.log""'", script, StringComparison.Ordinal);
        Assert.Contains(@"Remove-Item -LiteralPath 'C:\O''Brien\Steward-0.20.2.msi'", script, StringComparison.Ordinal);
    }

    private static readonly AppUpdater Updater = new(new HttpClient(), new Uri("https://addon.example/steward/latest.json"), "9ABC");

    private static AddonRelease Release(string version) => new(version, null, null, 0, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("1.2.3\\..\\x")]
    [InlineData("1.2.3/x")]
    [InlineData("1.2")]
    [InlineData("1.2.3 ")]
    [InlineData("1.2.3-a b")]
    [InlineData("C:1.2.3")]
    [InlineData("")]
    public async Task DownloadMsiAsync_RejectsAVersionThatIsNotStrictSemver(string version)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Updater.DownloadMsiAsync(Release(version), null, TestContext.Current.CancellationToken));

        Assert.Contains("not a version number", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.21.2")]
    [InlineData("v0.21.2")]
    [InlineData("0.21.2.0")]
    [InlineData("0.5.0-pre-release.219da29")]
    public async Task DownloadMsiAsync_AcceptsSemverAndFailsLaterOnTheMissingZip(string version)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Updater.DownloadMsiAsync(Release(version), null, TestContext.Current.CancellationToken));

        Assert.Contains("has no MSI", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallMsiScript_LeavesTheRelaunchToTheMsi()
    {
        var script = AppUpdater.InstallMsiScript(@"C:\Temp\Steward-0.20.2.msi", @"C:\Logs\update.log");

        Assert.DoesNotContain("Steward.App.exe", script, StringComparison.Ordinal);
    }

    private const string Version = "0.21.2";
    private const string Tag = "v0.21.2";
    private const string File = "Steward-0.21.2-x64.msi";
    private const string MsiUrl = $"https://github.com/hoobio/steward-companion/releases/download/{Tag}/{File}";
    private static readonly byte[] MsiBytes = "not really an msi"u8.ToArray();
    private static readonly string MsiSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(MsiBytes));

    private sealed class GitHubStub(Func<Uri, (HttpStatusCode Status, byte[] Body)> route) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var (status, body) = route(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    private static byte[] ReleaseJson(string? digest, string name = File, string url = MsiUrl) =>
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            tag_name = Tag,
            assets = new[] { new { name, browser_download_url = url, digest } },
        }));

    private static GitHubStub Stub(byte[] releaseJson, byte[]? msi = null, HttpStatusCode apiStatus = HttpStatusCode.OK) =>
        new(uri => uri.Host == "api.github.com"
            ? (apiStatus, releaseJson)
            : (HttpStatusCode.OK, msi ?? MsiBytes));

    private static Task<string> Download(GitHubStub stub, string version = Version, string zip = MsiUrl, string? sha = null) =>
        new AppUpdater(new HttpClient(stub), new Uri("https://api.example.com/guild/api/addons/steward-companion/latest-release.json"), "9ABC")
            .DownloadMsiAsync(new AddonRelease(version, zip, sha ?? MsiSha, MsiBytes.Length, DateTimeOffset.UtcNow), null, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(Version)]
    [InlineData(Tag)]
    public async Task DownloadMsiAsync_DigestMatchingManifestAndFile_Downloads(string version)
    {
        var stub = Stub(ReleaseJson($"sha256:{MsiSha}"));

        var path = await Download(stub, version);
        try
        {
            Assert.Equal(MsiBytes, await System.IO.File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            var api = stub.Requests[0];
            Assert.Equal($"https://api.github.com/repos/hoobio/steward-companion/releases/tags/{Tag}", api.RequestUri!.AbsoluteUri);
            Assert.Contains(api.Headers.Accept, accept => accept.MediaType == "application/vnd.github+json");
            Assert.NotEmpty(api.Headers.UserAgent);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Theory]
    [InlineData("https://evil.example/hoobio/steward-companion/releases/download/v9.8.7-test/Steward-9.8.7-test-x64.msi")]
    [InlineData("http://github.com/hoobio/steward-companion/releases/download/v9.8.7-test/Steward-9.8.7-test-x64.msi")]
    [InlineData("https://github.com/someone/steward-companion/releases/download/v9.8.7-test/Steward-9.8.7-test-x64.msi")]
    [InlineData("https://github.com/hoobio/steward-companion/releases/download/v9.8.7-test/Steward-9.8.7-test-x64.exe")]
    [InlineData("https://github.com/hoobio/steward-companion/releases/download/v9.8.7-test/Steward-9.8.7-test-x64.msi?x=1")]
    [InlineData("https://user@github.com/hoobio/steward-companion/releases/download/v9.8.7-test/Steward-9.8.7-test-x64.msi")]
    [InlineData("https://github.com:8443/hoobio/steward-companion/releases/download/v9.8.7-test/Steward-9.8.7-test-x64.msi")]
    [InlineData("https://github.com.evil.example/hoobio/steward-companion/releases/download/v9.8.7-test/Steward-9.8.7-test-x64.msi")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/x.msi")]
    public async Task DownloadMsiAsync_UrlOutsideTheGitHubRelease_IsRefusedBeforeAnyRequest(string url)
    {
        var stub = Stub(ReleaseJson($"sha256:{MsiSha}", url: url));

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Download(stub, zip: url));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task DownloadMsiAsync_TagDifferentFromManifestVersion_IsRefused()
    {
        var stub = Stub(ReleaseJson($"sha256:{MsiSha}"));

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Download(
            stub, zip: "https://github.com/hoobio/steward-companion/releases/download/v0.21.1/Steward-0.21.1-x64.msi"));
        Assert.Empty(stub.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("md5:abc")]
    [InlineData("sha256:")]
    public async Task DownloadMsiAsync_MissingOrUnusableDigest_IsRefusedWithoutDownloading(string? digest)
    {
        var stub = Stub(ReleaseJson(digest));

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Download(stub));
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task DownloadMsiAsync_AssetNotOnTheRelease_IsRefused()
    {
        var stub = Stub(ReleaseJson($"sha256:{MsiSha}", name: "Other.msi"));

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Download(stub));
    }

    [Fact]
    public async Task DownloadMsiAsync_DigestDifferentFromManifest_IsRefusedWithoutDownloading()
    {
        var stub = Stub(ReleaseJson($"sha256:{new string('0', 64)}"));

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Download(stub));
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task DownloadMsiAsync_DownloadedFileDifferentFromDigest_IsRefused()
    {
        var other = "tampered"u8.ToArray();
        var otherSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(other));
        var stub = Stub(ReleaseJson($"sha256:{otherSha}"));

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Download(stub, sha: otherSha));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task DownloadMsiAsync_GitHubApiFailure_IsRefused(HttpStatusCode status)
    {
        var stub = Stub(ReleaseJson($"sha256:{MsiSha}"), apiStatus: status);

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Download(stub));
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task DownloadMsiAsync_NonJsonApiAnswer_IsRefused()
    {
        var stub = Stub("<html>rate limited</html>"u8.ToArray());

        await Assert.ThrowsAsync<UpdateVerificationException>(() => Download(stub));
    }
}
