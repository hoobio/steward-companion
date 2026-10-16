namespace Steward.Core.Tests;

public sealed class WinePrefixesTests : IDisposable
{
    private static readonly Dictionary<string, string> SupportedProducts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wowf"] = "World of Warcraft: Forever",
        ["wow_classic_beta"] = "World of Warcraft: Forever - Beta",
    };

    private readonly string _home = Directory.CreateTempSubdirectory("steward-home-").FullName;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private static string Wow(string prefix, string programFiles = "Program Files (x86)") =>
        Directory.CreateDirectory(Path.Combine(prefix, "drive_c", programFiles, "World of Warcraft")).FullName;

    private string Home(params string[] parts) => Path.Combine([_home, .. parts]);

    [Fact]
    public void WowRoots_FindsTheClientInEveryLauncherPrefix()
    {
        var custom = Home("elsewhere", "bnet");
        var expected = new[]
        {
            Wow(Home("wineprefix")),
            Wow(Home(".wine"), "Program Files"),
            Wow(custom),
            Wow(Home("Games", "battlenet")),
            Wow(Home("Games", "Heroic", "Prefixes", "default", "wow")),
            Wow(Home("Faugus", "battlenet")),
            Wow(Home(".local", "share", "bottles", "bottles", "Battle-net")),
            Wow(Home(".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles", "wow")),
            Wow(Home(".steam", "steam", "steamapps", "compatdata", "123", "pfx")),
            Wow(Home(".local", "share", "Steam", "steamapps", "compatdata", "456", "pfx")),
            Wow(Home(".var", "app", "com.valvesoftware.Steam", "data", "Steam", "steamapps", "compatdata", "789", "pfx")),
        };
        Directory.CreateDirectory(Home(".config", "lutris", "games"));
        File.WriteAllText(Home(".config", "lutris", "games", "battlenet-1700000000.yml"), $"game:\n  exe: drive_c/Program Files (x86)/Battle.net/Battle.net Launcher.exe\n  prefix: '{custom}'\ngame_slug: battlenet\n");
        Directory.CreateDirectory(Home("Games", "empty"));

        var roots = WinePrefixes.WowRoots(_home, name => name == "WINEPREFIX" ? Home("wineprefix") : null).ToList();

        Assert.Equal(expected.Order(StringComparer.Ordinal), roots.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WowRoots_MatchesFolderNamesIgnoringCase()
    {
        Directory.CreateDirectory(Home(".wine", "drive_c", "program files (x86)", "world of warcraft"));

        var root = Assert.Single(WinePrefixes.WowRoots(_home, _ => null));

        Assert.Equal(Home(".wine", "drive_c", "Program Files (x86)", "World of Warcraft"), root, ignoreCase: true);
    }

    [Fact]
    public void WowRoots_FeedTheFlavourWalk()
    {
        var root = Wow(Home("Games", "battlenet"));
        var flavour = Directory.CreateDirectory(Path.Combine(root, "_forever_")).FullName;
        File.WriteAllText(Path.Combine(flavour, ".flavor.info"), "Product Flavor!STRING:0\nwowf\n");
        File.WriteAllText(
            Path.Combine(root, ".build.info"),
            "Branch!STRING:0|Active!DEC:1|Build Key!HEX:16|Version!STRING:0|KeyRing!HEX:16|Product!STRING:0\nus|1|2582|1.15.8.63829||wowf\n");

        var install = Assert.Single(WinePrefixes.WowRoots(_home, _ => null).SelectMany(found => WowInstalls.DiscoverAt(found, SupportedProducts)));

        Assert.Equal(flavour, install.FlavourPath);
        Assert.Equal("wowf", install.ProductCode);
        Assert.Equal("1.15.8.63829", install.ClientVersion);
    }

    [Theory]
    [InlineData("game:\n  prefix: /games/bnet\n", "/games/bnet")]
    [InlineData("game:\r\n  prefix: \"/games/battle net\"\r\n", "/games/battle net")]
    [InlineData("game:\n  exe: x.exe\n", null)]
    [InlineData("game:\n  prefix: relative/bnet\n", null)]
    public void LutrisPrefix_ReadsTheGamePrefix(string yaml, string? expected)
    {
        Assert.Equal(expected is null ? null : Path.GetFullPath(expected), WinePrefixes.LutrisPrefix(yaml, _home) is { } prefix ? Path.GetFullPath(prefix) : null);
    }

    [Fact]
    public void LutrisPrefix_ExpandsTheHomeFolder()
    {
        Assert.Equal(_home + "/Games/battlenet", WinePrefixes.LutrisPrefix("game:\n  prefix: ~/Games/battlenet\n", _home));
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\World of Warcraft\_forever_\Wow.exe", true)]
    [InlineData(@"c:/windows/system32", true)]
    [InlineData(@"Z:\home\u\Wow.exe", true)]
    [InlineData(@"D:\Wow.exe", false)]
    [InlineData(@"Wow.exe", false)]
    [InlineData(@"\\server\share\Wow.exe", false)]
    public void ToUnixPath_MapsTheWineDrives(string windowsPath, bool mapped)
    {
        var prefix = Home("prefix");
        var expected = windowsPath[..1].ToUpperInvariant() switch
        {
            "C" => Path.Combine([prefix, "drive_c", .. windowsPath[3..].Split('\\', '/')]),
            "Z" => Path.Combine(["/", .. windowsPath[3..].Split('\\', '/')]),
            _ => null,
        };

        Assert.Equal(mapped ? expected : null, WinePrefixes.ToUnixPath(windowsPath, prefix));
    }
}
