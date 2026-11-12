namespace Steward.Core.Tests;

public sealed class VersionLabelTests
{
    [Theory]
    [InlineData("BetterForeverChat-0.12.6-alpha.zip", "BetterForeverChat", "0.12.6-alpha")]
    [InlineData("RareScanner_FOREVER_1.60.1.b6", "RareScanner", "1.60.1.b6")]
    [InlineData("v8.0.1-forever", "Gargul", "v8.0.1-forever")]
    [InlineData("0.5.0-pre-release.219da29", "Steward", "0.5.0-219da29")]
    [InlineData("Questie", "Questie", "Questie")]
    [InlineData("v2.4.13-forever-20261110-1a2b3c", "AtlasLootClassic", "v2.4.13-f…1a2b3c")]
    [InlineData("0.1.2.3.4.5.6.78", "Steward", "0.1.2.3.4.5.6.78")]
    public void For_DropsTheAddonNameAndZipExtension(string version, string folder, string expected) =>
        Assert.Equal(expected, VersionLabel.For(version, folder));

    [Fact]
    public void For_NoVersion_ReturnsNull() => Assert.Null(VersionLabel.For(null, "Steward"));
}
