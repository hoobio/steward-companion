using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class ChangelogsTests
{
    private const string Manifest = """{"version":"1.2.0","zip":"a.zip","sha256":"ab","size":10,"released":"2026-09-28T00:00:00Z"%EXTRA%}""";

    private static AddonRelease Parse(string extra) =>
        JsonSerializer.Deserialize(Manifest.Replace("%EXTRA%", extra, StringComparison.Ordinal), CompanionJsonContext.Default.AddonRelease)!;

    [Fact]
    public void Parses_StructuredChangelog()
    {
        var release = Parse(""","changelog":[{"kind":"heading","level":2,"runs":[{"text":"Fixes"}]},{"kind":"item","depth":1,"runs":[{"text":"See "},{"text":"docs","href":"https://example.com"}]}]""");

        var blocks = Changelogs.For(release);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("heading", blocks[0].Kind);
        Assert.Equal(2, blocks[0].Level);
        Assert.Equal(1, blocks[1].Depth);
        Assert.Equal("https://example.com", blocks[1].Runs![1].Href);
    }

    [Fact]
    public void FallsBackToNotes_AsDepthZeroItems()
    {
        var blocks = Changelogs.For(Parse(""","notes":["One","Two"]"""));

        Assert.Equal(["One", "Two"], blocks.Select(block => block.Runs![0].Text));
        Assert.All(blocks, block =>
        {
            Assert.Equal("item", block.Kind);
            Assert.Equal(0, block.Depth);
        });
    }

    [Fact]
    public void EmptyChangelog_FallsBackToNotes()
    {
        var blocks = Changelogs.For(Parse(""","changelog":[],"notes":["One"]"""));

        Assert.Single(blocks);
    }

    [Fact]
    public void UnknownKind_ParsesWithoutThrowing()
    {
        var blocks = Changelogs.For(Parse(""","changelog":[{"kind":"quote","runs":[{"text":"x"}]}]"""));

        Assert.Equal("quote", blocks[0].Kind);
    }

    [Fact]
    public void NoRelease_GivesNoBlocks()
    {
        Assert.Empty(Changelogs.For(null));
    }
}
