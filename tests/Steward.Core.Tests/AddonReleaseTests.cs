using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class AddonReleaseTests
{
    private const string Manifest = """{"version":"1.2.0","zip":"a.zip","sha256":"ab","size":10,"released":"2026-09-28T00:00:00Z"%NOTES%}""";

    [Fact]
    public void Parses_Notes_WhenPresent()
    {
        var release = JsonSerializer.Deserialize(Manifest.Replace("%NOTES%", ""","notes":["Fixed a crash","Added a button"]""", StringComparison.Ordinal), CompanionJsonContext.Default.AddonRelease);

        Assert.Equal<IEnumerable<string>>(["Fixed a crash", "Added a button"], release!.Notes!);
    }

    [Fact]
    public void Parses_WithoutNotes()
    {
        var release = JsonSerializer.Deserialize(Manifest.Replace("%NOTES%", "", StringComparison.Ordinal), CompanionJsonContext.Default.AddonRelease);

        Assert.Equal("1.2.0", release!.Version);
        Assert.Null(release.Notes);
        Assert.Equal("ab", release.Sha256);
        Assert.Null(release.Sha1);
        Assert.Null(release.Folders);
        Assert.Null(release.Website);
        Assert.True(release.Distributable);
    }

    [Fact]
    public void Parses_CurseForgeManifest()
    {
        const string json = """{"version":"v12.0.2+v1.0.4","zip":"https://edge.forgecdn.net/files/8997/556/Questie.zip","sha1":"f74fe2db5e253bfa80994d5bd4705efac9317d46","size":91715613,"released":"2026-09-28T06:21:31Z","folders":["QuestieDB","Questie"],"notes":["Fix"],"website":"https://www.curseforge.com/wow/addons/questie","distributable":true}""";

        var release = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.AddonRelease)!;

        Assert.Null(release.Sha256);
        Assert.Equal("f74fe2db5e253bfa80994d5bd4705efac9317d46", release.Sha1);
        Assert.Equal<IEnumerable<string>>(["QuestieDB", "Questie"], release.Folders!);
        Assert.Equal("https://www.curseforge.com/wow/addons/questie", release.Website);
        Assert.True(release.Distributable);
    }

    [Fact]
    public void Parses_NonDistributableManifest_WithoutZipOrHash()
    {
        const string json = """{"version":"1.0","size":0,"released":"2026-09-28T00:00:00Z","website":"https://www.curseforge.com/wow/addons/dbm","distributable":false}""";

        var release = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.AddonRelease)!;

        Assert.False(release.Distributable);
        Assert.Null(release.Zip);
        Assert.Null(release.Sha1);
    }
}
