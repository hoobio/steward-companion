namespace Steward.Core.Tests;

public sealed class RosterNotesTests
{
    [Fact]
    public void Trim_YOnly_ReturnsEmpty() => Assert.Equal(string.Empty, RosterNotes.Trim("Y"));

    [Fact]
    public void Trim_NOnly_ReturnsEmpty() => Assert.Equal(string.Empty, RosterNotes.Trim("N"));

    [Fact]
    public void Trim_YThenNote_DropsTheYLine() => Assert.Equal("Hoobi", RosterNotes.Trim("Y\nHoobi"));

    [Fact]
    public void Trim_NoYOrNLines_LeavesTheNoteUnchanged() =>
        Assert.Equal("Careful with this one", RosterNotes.Trim("Careful with this one"));

    [Fact]
    public void Trim_EmptyNote_ReturnsEmpty() => Assert.Equal(string.Empty, RosterNotes.Trim(string.Empty));

    [Fact]
    public void Trim_LineStartingWithY_SurvivesUntouched() =>
        Assert.Equal("Yes please", RosterNotes.Trim("Yes please"));

    [Fact]
    public void Trim_Null_ReturnsNull() => Assert.Null(RosterNotes.Trim(null));
}
