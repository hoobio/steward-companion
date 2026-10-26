using System.IO.Compression;
using System.Net;

namespace Steward.Core.Tests;

public sealed class TukUiSourceTests : IDisposable
{
    private const string List = """
        [
          {"id":-2,"slug":"elvui","author":"Elv","name":"ElvUI","url":"https://api.tukui.org/v1/download/elvui/TOKEN","version":"16.00","changelog_url":"https://api.tukui.org/v1/changelog/elvui","ticket_url":"https://github.com/tukui-org/ElvUI/issues","git_url":"https://github.com/tukui-org/ElvUI","patch":["12.1.0","5.5.4","1.15.9","2.5.6"],"last_update":"2026-10-13","web_url":"https://tukui.org/elvui","donate_url":"http://tukui.org/support","small_desc":"A user interface designed around user-friendliness with extra features that are not included in the standard UI.","desc":"## About","screenshot_url":"https://storage.tukui.org/ElvUI-1.jpg","gallery_url":["https://storage.tukui.org/ElvUI-2.jpg"],"logo_url":"https://storage.tukui.org/ElvUI-Logo.png","logo_square_url":"https://storage.tukui.org/ElvUI-Logo-Square.png","directories":["ElvUI","ElvUI_Libraries","ElvUI_Options"]},
          {"id":-1,"slug":"tukui","author":"Tukz","name":"Tukui","url":"https://api.tukui.org/v1/download/tukui/TOKEN","version":"20.463","changelog_url":"https://api.tukui.org/v1/changelog/tukui","patch":["11.1.7","5.5.0","1.15.7"],"last_update":"2025-12-05","web_url":"https://tukui.org/tukui","small_desc":"Minimalistic and lightweight World of Warcraft user interface.","logo_square_url":"https://storage.tukui.org/Tukui-Logo-Square.png","directories":["Tukui"]}
        ]
        """;

    private const string Changelog = """
        ### Version 16.00 [ October 13th 2026 ]
        * **After almost two months of pretty much daily work and testing, we're introducing our 16.00 update for ElvUI:**
            *   A large performance pass and many fixes.
            *   This release also adds initial support for the upcoming WoW: Forever game version.

        ### Version 15.12 [ September 1st 2026 ]
        * Older fix
        """;

    private static readonly ManagedAddon ElvUIRow = TukUiAddons.ToManagedAddon(new ProviderAddonRecord("tukui--2", "ElvUI", "ElvUI", TukUiAddons.Source, -2, 0, ["ElvUI", "ElvUI_Libraries", "ElvUI_Options"]));

    private readonly string _tempDir = Directory.CreateTempSubdirectory("steward-tukui-source-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void ToRelease_MapsAListEntryToTheUpdatersReleaseShape()
    {
        var addon = Parse()[0];

        var release = TukUiSource.ToRelease(addon, ["note"]);

        Assert.Equal("16.00", release.Version);
        Assert.Equal("https://api.tukui.org/v1/download/elvui/TOKEN", release.Zip);
        Assert.Null(release.Sha256);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero), release.Released);
        Assert.Equal(["note"], release.Notes!);
        Assert.Equal(["ElvUI", "ElvUI_Libraries", "ElvUI_Options"], release.Folders!);
        Assert.Equal("https://tukui.org/elvui", release.Website);
        Assert.Equal(-2, release.TukUiProjectId);
        Assert.Equal("https://storage.tukui.org/ElvUI-Logo-Square.png", TukUiAddons.Record(addon).IconUrl);
    }

    [Fact]
    public void ToRelease_DropsALeadingV_AndRejectsAnEntryWithNoDate()
    {
        var addon = Parse()[0] with { Version = "v16.01" };

        Assert.Equal("16.01", TukUiSource.ToRelease(addon, []).Version);
        Assert.Throws<System.Text.Json.JsonException>(() => TukUiSource.ToRelease(addon with { LastUpdate = "13/10/2026" }, []));
    }

    [Fact]
    public void Notes_TakesTheNewestSectionOnly()
    {
        Assert.Equal(
            [
                "After almost two months of pretty much daily work and testing, we're introducing our 16.00 update for ElvUI:",
                "A large performance pass and many fixes.",
                "This release also adds initial support for the upcoming WoW: Forever game version.",
            ],
            TukUiSource.Notes(Changelog.Replace("\r\n", "\n", StringComparison.Ordinal)));
        Assert.Empty(TukUiSource.Notes("no headings here"));
    }

    [Theory]
    [InlineData(-2, "1.60.1.70245", true)]
    [InlineData(-2, "12.0.1.6", true)]
    [InlineData(-1, "12.0.1.6", false)]
    [InlineData(-1, "2.5.5.1", false)]
    [InlineData(-1, "1.15.8.1", true)]
    [InlineData(-1, null, true)]
    public void RunsOn_MatchesTheClientsMajorVersionAgainstTheListedPatches(int id, string? clientVersion, bool runs) =>
        Assert.Equal(runs, TukUiSource.RunsOn(Parse().Single(addon => addon.Id == id), clientVersion));

    [Fact]
    public async Task The_list_is_cached_for_15_minutes_and_a_refresh_refetches_one_a_minute_old()
    {
        var (source, handler, clock) = Source();

        await source.ReleaseAsync(-2, noCache: false, TestContext.Current.CancellationToken);
        clock.Now += TimeSpan.FromMinutes(14);
        await source.ReleaseAsync(-2, noCache: false, TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.ListCalls);

        await source.ReleaseAsync(-2, noCache: true, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.ListCalls);

        clock.Now += TimeSpan.FromSeconds(59);
        await source.ReleaseAsync(-2, noCache: true, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.ListCalls);

        clock.Now += TimeSpan.FromMinutes(15);
        await source.ReleaseAsync(-2, noCache: false, TestContext.Current.CancellationToken);
        Assert.Equal(3, handler.ListCalls);
        Assert.Equal(1, handler.ChangelogCalls);
    }

    [Fact]
    public async Task A_download_takes_its_token_from_a_list_at_most_60_seconds_old()
    {
        var (source, handler, clock) = Source();
        await source.ReleaseAsync(-2, noCache: false, TestContext.Current.CancellationToken);

        clock.Now += TimeSpan.FromSeconds(59);
        Assert.Equal("https://api.tukui.org/v1/download/elvui/TOKEN1", (await source.ZipUriAsync(-2, "16.00", TestContext.Current.CancellationToken)).AbsoluteUri);
        Assert.Equal(1, handler.ListCalls);

        clock.Now += TimeSpan.FromSeconds(2);
        Assert.Equal("https://api.tukui.org/v1/download/elvui/TOKEN2", (await source.ZipUriAsync(-2, "16.00", TestContext.Current.CancellationToken)).AbsoluteUri);
        Assert.Equal(2, handler.ListCalls);
    }

    [Fact]
    public async Task A_download_refuses_when_TukUI_now_lists_another_version()
    {
        var (source, _, _) = Source();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.ZipUriAsync(-2, "15.12", TestContext.Current.CancellationToken));

        Assert.Contains("16.00", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_TukUI_throws_rather_than_reading_as_no_release()
    {
        var (source, handler, _) = Source();
        handler.ListStatus = HttpStatusCode.BadGateway;

        await Assert.ThrowsAsync<HttpRequestException>(() => source.ReleaseAsync(-2, noCache: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failed_changelog_gives_no_notes_and_is_retried_on_the_next_check()
    {
        var (source, handler, _) = Source();
        handler.ChangelogStatus = HttpStatusCode.InternalServerError;

        Assert.Empty((await source.ReleaseAsync(-2, noCache: false, TestContext.Current.CancellationToken))!.Notes!);

        handler.ChangelogStatus = HttpStatusCode.OK;
        Assert.NotEmpty((await source.ReleaseAsync(-2, noCache: false, TestContext.Current.CancellationToken))!.Notes!);
        Assert.Equal(2, handler.ChangelogCalls);
    }

    [Fact]
    public async Task The_updater_reads_a_TukUI_row_from_TukUI_with_no_pre_release_channel()
    {
        var (handler, updater, _) = Updater();

        var releases = await updater.ProbeChannelsAsync(ElvUIRow, AddonChannelStatus.Ordered, TestContext.Current.CancellationToken);

        Assert.Equal("16.00", releases["release"]!.Version);
        Assert.Null(releases["pre-release"]);
        Assert.All(handler.Requests, uri => Assert.Equal("api.tukui.org", uri.Host));
    }

    [Fact]
    public async Task The_updater_installs_a_TukUI_release_without_a_hash_from_a_fresh_token()
    {
        var (handler, updater, clock) = Updater();
        var release = await updater.GetLatestAsync(ElvUIRow, "release", TestContext.Current.CancellationToken);
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        Directory.CreateDirectory(addOnsPath);

        clock.Now += TimeSpan.FromMinutes(10);
        var folders = await updater.InstallAsync(ElvUIRow, "release", release!, addOnsPath, null, TestContext.Current.CancellationToken);

        Assert.Equal(["ElvUI", "ElvUI_Libraries", "ElvUI_Options"], folders);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "ElvUI", "ElvUI.toc")));
        Assert.Equal("/v1/download/elvui/TOKEN2", handler.Requests.Last().AbsolutePath);
    }

    [Fact]
    public async Task A_TukUI_row_with_a_hand_written_release_and_no_hash_is_still_refused()
    {
        var (handler, updater, _) = Updater();
        var addOnsPath = Path.Combine(_tempDir, "AddOns");
        var release = new AddonRelease("16.00", "https://api.tukui.org/v1/download/elvui/TOKEN", null, 0, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => updater.InstallAsync(ElvUIRow, "release", release, addOnsPath, null, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    private static TukUiAddon[] Parse() =>
        System.Text.Json.JsonSerializer.Deserialize(List, CompanionJsonContext.Default.TukUiAddonArray)!;

    private static (TukUiSource Source, TukUiHandler Handler, FakeClock Clock) Source()
    {
        var handler = new TukUiHandler([]);
        var clock = new FakeClock();
        return (new TukUiSource(new HttpClient(handler), clock), handler, clock);
    }

    private static (TukUiHandler Handler, AddonUpdater Updater, FakeClock Clock) Updater()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var folder in new[] { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" })
            {
                using var writer = new StreamWriter(archive.CreateEntry($"{folder}/{folder}.toc").Open());
                writer.Write("## Version: v16.00");
            }
        }

        var handler = new TukUiHandler(stream.ToArray());
        var clock = new FakeClock();
        return (handler, new AddonUpdater(new HttpClient(handler), clock: clock), clock);
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TukUiHandler(byte[] zip) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public HttpStatusCode ListStatus { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode ChangelogStatus { get; set; } = HttpStatusCode.OK;

        public int ListCalls => Requests.Count(uri => uri == TukUiSource.ListUri);

        public int ChangelogCalls => Requests.Count(uri => uri.AbsolutePath.StartsWith("/v1/changelog/", StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            return Task.FromResult(uri.AbsolutePath switch
            {
                "/v1/addons" when ListStatus == HttpStatusCode.OK => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(List.Replace("TOKEN", $"TOKEN{ListCalls}", StringComparison.Ordinal)) },
                "/v1/addons" => new HttpResponseMessage(ListStatus),
                var path when path.StartsWith("/v1/changelog/", StringComparison.Ordinal) => new HttpResponseMessage(ChangelogStatus) { Content = new StringContent(Changelog) },
                var path when path.StartsWith("/v1/download/", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }
    }
}
