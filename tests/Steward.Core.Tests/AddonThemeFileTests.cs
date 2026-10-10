using System.Text;

namespace Steward.Core.Tests;

public sealed class AddonThemeFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-theme-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Placeholder(string folder)
    {
        var path = Path.Combine(_root, folder, "Core", "Theme.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "local _, ns = ...\nns.themeId = nil\n");
        return path;
    }

    [Fact]
    public void Write_ReplacesThePlaceholder_WithLfAndNoBom()
    {
        var path = Placeholder("HoobiScripts");

        Assert.True(AddonThemeFile.Write(_root, "HoobiScripts", "123456789"));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal("local _, ns = ...\nns.themeId = \"123456789\"\n", Encoding.UTF8.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_AgainWithTheSameId_ChangesNothing()
    {
        Placeholder("HoobiScripts");
        AddonThemeFile.Write(_root, "HoobiScripts", "1");

        Assert.False(AddonThemeFile.Write(_root, "HoobiScripts", "1"));
        Assert.True(AddonThemeFile.Write(_root, "HoobiScripts", "2"));
    }

    [Fact]
    public void Write_WithoutAnIdOrAPlaceholder_DoesNothing()
    {
        Placeholder("HoobiScripts");

        Assert.False(AddonThemeFile.Write(_root, "HoobiScripts", null));
        Assert.False(AddonThemeFile.Write(_root, "Other", "1"));
        Assert.False(File.Exists(Path.Combine(_root, "Other", "Core", "Theme.lua")));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("..\\Elsewhere")]
    [InlineData("A/B")]
    public void Write_RefusesAFolderThatIsNotOneSegment(string folder) =>
        Assert.Throws<InvalidOperationException>(() => AddonThemeFile.Write(_root, folder, "1"));

    [Fact]
    public void AppliesTo_NoticeSessionManifestOrProtectedSource()
    {
        var plain = new ManagedAddon("a", "A", "https://example.test/a/");

        Assert.False(AddonThemeFile.AppliesTo(plain));
        Assert.True(AddonThemeFile.AppliesTo(plain with { Notice = new AddonNotice("lock", "x") }));
        Assert.True(AddonThemeFile.AppliesTo(plain with { ManifestAuth = "session" }));
        Assert.True(AddonThemeFile.AppliesTo(plain with { Source = AddonCatalogue.ProtectedSource }));
    }
}
