namespace Steward.Core.Tests;

public sealed class AddonSearchTests
{
    [Theory]
    [InlineData("RestedXP Guides", "res")]
    [InlineData("RestedXP Guides", "gui")]
    [InlineData("RareScanner", "scan")]
    [InlineData("Gargul | Master Loot Tool for GDKP, SoftRes", "res")]
    [InlineData("DBM-Core", "core")]
    [InlineData("Details!", "")]
    [InlineData("AtlasLoot2", "2")]
    public void Matches_AtAWordStart(string name, string query) => Assert.True(AddonSearch.Matches(name, query));

    [Theory]
    [InlineData("RareScanner", "res")]
    [InlineData("Questie", "est")]
    [InlineData("RestedXP Guides", "xyz")]
    public void Matches_InsideAWord_IsFalse(string name, string query) => Assert.False(AddonSearch.Matches(name, query));

    [Fact]
    public void Find_PrefersAWordStartOverAnEarlierMidWordHit() =>
        Assert.Equal(new SearchMatch(12, 3), AddonSearch.Find("RareScanner Results", "res"));

    [Fact]
    public void Find_NoWordStart_ReturnsTheFirstSubstringHit() =>
        Assert.Equal(new SearchMatch(5, 2), AddonSearch.Find("BugSack", "ck"));

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    public void Find_NoMatch_ReturnsNull(string query) => Assert.Null(AddonSearch.Find("BugSack", query));

    private static readonly string[] Names = ["RestedXP Guides", "RareScanner", "BugSack", "BugGrabber"];

    [Fact]
    public void Filter_WordStartMatches_HideMidWordOnes() =>
        Assert.Equal(["RestedXP Guides"], AddonSearch.Filter(Names, name => name, "res"));

    [Fact]
    public void Filter_NoWordStartMatch_FallsBackToSubstring() =>
        Assert.Equal(["BugSack"], AddonSearch.Filter(Names, name => name, "ck"));
}
