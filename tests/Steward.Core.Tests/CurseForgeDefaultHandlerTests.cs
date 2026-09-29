namespace Steward.Core.Tests;

public sealed class CurseForgeDefaultHandlerTests
{
    private const string Ours = "Hoobi.Steward_thayxpy3eqg0g!App";

    [Theory]
    [InlineData("AppX123", "hoobi.steward_thayxpy3eqg0g!app", true)]
    [InlineData("AppX123", "Other_abc!App", false)]
    [InlineData("AppX123", null, false)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    public void IsOurs_ComparesResolvedAumid(string? progId, string? aumid, bool expected) =>
        Assert.Equal(expected, CurseForgeDefaultHandler.IsOurs(progId, Ours, _ => aumid));
}
