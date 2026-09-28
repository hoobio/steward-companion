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
    }
}
