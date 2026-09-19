namespace Steward.Core.Tests;

public sealed class WowInstallsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-wow-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string CreateValidFlavourDir(string name, string productCode)
    {
        var flavourPath = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(flavourPath, "Interface", "AddOns"));
        File.WriteAllText(
            Path.Combine(flavourPath, ".flavor.info"),
            $"Product Flavor!STRING:0\n{productCode}\n");
        return flavourPath;
    }

    private void WriteBuildInfo(string productCode, string version)
    {
        File.WriteAllText(
            Path.Combine(_root, ".build.info"),
            $"Product!STRING:0|Version!STRING:0|Branch!STRING:0\n{productCode}|{version}|beta\n");
    }

    [Fact]
    public void DiscoverAt_FindsOnlyStructurallyValidFlavourDirs()
    {
        CreateValidFlavourDir("_classic_beta_", "wow_classic_beta");
        Directory.CreateDirectory(Path.Combine(_root, "_retail_"));

        var installs = WowInstalls.DiscoverAt(_root).ToList();

        var install = Assert.Single(installs);
        Assert.Equal("_classic_beta_", install.Flavour);
        Assert.Equal(Path.Combine(_root, "_classic_beta_", "Interface", "AddOns"), install.AddOnsPath);
    }

    [Fact]
    public void DiscoverAt_ResolvesProductCodeAndClientVersion_WithNonDefaultColumnOrder()
    {
        CreateValidFlavourDir("_classic_beta_", "wow_classic_beta");
        WriteBuildInfo("wow_classic_beta", "1.15.7.60000");

        var install = Assert.Single(WowInstalls.DiscoverAt(_root));

        Assert.Equal("wow_classic_beta", install.ProductCode);
        Assert.Equal("1.15.7.60000", install.ClientVersion);
    }

    [Fact]
    public void FromFlavourPath_ReturnsNull_ForInvalidPath()
    {
        var invalid = Path.Combine(_root, "_retail_");
        Directory.CreateDirectory(invalid);

        Assert.Null(WowInstalls.FromFlavourPath(invalid));
    }

    [Fact]
    public void FromFlavourPath_BuildsInstall_ForValidPath()
    {
        var flavourPath = CreateValidFlavourDir("_classic_beta_", "wow_classic_beta");

        var install = WowInstalls.FromFlavourPath(flavourPath);

        Assert.NotNull(install);
        Assert.Equal(flavourPath, install.FlavourPath);
    }

    [Theory]
    [InlineData("_classic_beta_", "World of Warcraft: Forever - Beta")]
    [InlineData("_retail_", "World of Warcraft")]
    [InlineData("_classic_era_", "_classic_era_")]
    public void DisplayName_MapsKnownFlavours_AndFallsBackToTheFolderName(string flavour, string expected) =>
        Assert.Equal(expected, WowInstalls.DisplayName(flavour));
}
