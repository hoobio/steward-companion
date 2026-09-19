namespace Steward.Core.Tests;

public sealed class WowInstallsTests : IDisposable
{
    private static readonly Dictionary<string, string> SupportedProducts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wow_classic_beta"] = "World of Warcraft: Forever - Beta",
    };

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

        var installs = WowInstalls.DiscoverAt(_root, SupportedProducts).ToList();

        var install = Assert.Single(installs);
        Assert.Equal("_classic_beta_", install.Flavour);
        Assert.Equal(Path.Combine(_root, "_classic_beta_", "Interface", "AddOns"), install.AddOnsPath);
    }

    [Fact]
    public void DiscoverAt_ResolvesProductCodeAndClientVersion_WithNonDefaultColumnOrder()
    {
        CreateValidFlavourDir("_classic_beta_", "wow_classic_beta");
        WriteBuildInfo("wow_classic_beta", "1.15.7.60000");

        var install = Assert.Single(WowInstalls.DiscoverAt(_root, SupportedProducts));

        Assert.Equal("wow_classic_beta", install.ProductCode);
        Assert.Equal("1.15.7.60000", install.ClientVersion);
    }

    [Fact]
    public void DiscoverAt_SkipsUnsupportedProducts()
    {
        CreateValidFlavourDir("_classic_beta_", "wow_classic_beta");
        CreateValidFlavourDir("_retail_", "wow");

        var installs = WowInstalls.DiscoverAt(_root, SupportedProducts).ToList();

        var install = Assert.Single(installs);
        Assert.Equal("_classic_beta_", install.Flavour);
    }

    [Fact]
    public void FromFlavourPath_ReturnsNull_ForInvalidPath()
    {
        var invalid = Path.Combine(_root, "_retail_");
        Directory.CreateDirectory(invalid);

        Assert.Null(WowInstalls.FromFlavourPath(invalid, SupportedProducts));
    }

    [Fact]
    public void FromFlavourPath_BuildsInstall_ForValidPath()
    {
        var flavourPath = CreateValidFlavourDir("_classic_beta_", "wow_classic_beta");

        var install = WowInstalls.FromFlavourPath(flavourPath, SupportedProducts);

        Assert.NotNull(install);
        Assert.Equal(flavourPath, install.FlavourPath);
    }

    [Fact]
    public void FromFlavourPath_ReturnsInstall_ForUnsupportedProduct()
    {
        var flavourPath = CreateValidFlavourDir("_retail_", "wow");

        var install = WowInstalls.FromFlavourPath(flavourPath, SupportedProducts);

        Assert.NotNull(install);
        Assert.Equal("wow", install.ProductCode);
        Assert.False(SupportedProducts.ContainsKey(install.ProductCode!));
    }

    [Fact]
    public void DisplayName_UsesTheProductMap_AndFallsBackToTheFolder()
    {
        var supported = CreateValidFlavourDir("_classic_beta_", "wow_classic_beta");
        var unsupported = CreateValidFlavourDir("_retail_", "wow");

        Assert.Equal("World of Warcraft: Forever - Beta", WowInstalls.FromFlavourPath(supported, SupportedProducts)!.DisplayName);
        Assert.Equal("_retail_", WowInstalls.FromFlavourPath(unsupported, SupportedProducts)!.DisplayName);
    }
}
