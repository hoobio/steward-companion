namespace Steward.Core.Tests;

public sealed class VersionLabelTests
{
    [Theory]
    [InlineData("BetterForeverChat-0.12.6-alpha.zip", "BetterForeverChat", "0.12.6-alpha")]
    [InlineData("RareScanner_FOREVER_1.60.1.b6", "RareScanner", "1.60.1.b6")]
    [InlineData("v8.0.1-forever", "Gargul", "v8.0.1-forever")]
    [InlineData("0.5.0-pre-release.219da29", "Steward", "0.5.0-219da29")]
    [InlineData("Questie", "Questie", "Questie")]
    public void For_DropsTheAddonNameAndZipExtension(string version, string folder, string expected) =>
        Assert.Equal(expected, VersionLabel.For(version, folder));

    [Fact]
    public void For_NoVersion_ReturnsNull() => Assert.Null(VersionLabel.For(null, "Steward"));
}
