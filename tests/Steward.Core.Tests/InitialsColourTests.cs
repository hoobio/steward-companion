namespace Steward.Core.Tests;

public sealed class InitialsColourTests
{
    [Fact]
    public void IndexFor_IsStableAcrossRunsAndCaseInsensitive()
    {
        Assert.Equal(1, InitialsColour.IndexFor("HoobiVersions", 8));
        Assert.Equal(1, InitialsColour.IndexFor("hoobiversions", 8));
    }

    [Fact]
    public void IndexFor_StaysInRange()
    {
        foreach (var name in new[] { "", "a", "DBM-Core", "!BugGrabber", "Leatrix_Plus", "WeakAuras" })
        {
            Assert.InRange(InitialsColour.IndexFor(name, 8), 0, 7);
        }
    }
}
