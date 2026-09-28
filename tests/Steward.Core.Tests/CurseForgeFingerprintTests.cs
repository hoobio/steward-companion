using System.Globalization;
using System.Text;

namespace Steward.Core.Tests;

public sealed class CurseForgeFingerprintTests : IDisposable
{
    private readonly string _addOnsPath = Directory.CreateTempSubdirectory("steward-fingerprint-").FullName;

    public void Dispose() => Directory.Delete(_addOnsPath, recursive: true);

    [Theory]
    [InlineData("", 1540447798u)]
    [InlineData("a", 626045324u)]
    [InlineData("abc", 1621425345u)]
    [InlineData("abcd", 3376380438u)]
    [InlineData("abcde", 3469237630u)]
    [InlineData("hello world", 2824650221u)]
    [InlineData(" \t\r\nhello\r\n world \t", 2824650221u)]
    [InlineData("The quick brown fox jumps over the lazy dog", 3751777527u)]
    public void Hash_MatchesWowUpComputeHash(string input, uint expected)
    {
        Assert.Equal(expected, CurseForgeFingerprint.Hash(Encoding.ASCII.GetBytes(input)));
    }

    [Fact]
    public void Hash_HighBytes_MatchWowUpComputeHash()
    {
        Assert.Equal(355099671u, CurseForgeFingerprint.Hash([0xC3, 0xA9, 0xFF, 0x80]));
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_addOnsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static uint Expected(IEnumerable<string> files) =>
        CurseForgeFingerprint.Hash(Encoding.ASCII.GetBytes(string.Concat(
            files.Select(file => CurseForgeFingerprint.Hash(File.ReadAllBytes(file))).Order().Select(hash => hash.ToString(CultureInfo.InvariantCulture)))));

    [Fact]
    public void Compute_SelectsTocsBindingsAndRecursiveIncludes()
    {
        string[] included =
        [
            Write(@"Addon\Addon.toc", "## Interface: 11507\n# a comment\nCore.lua # trailing\nui\\Frames.xml\n..\\Other\\Evil.lua\nmissing.lua\nLOCALE.LUA\n"),
            Write(@"Addon\Addon_Vanilla.toc", "Vanilla.lua\n"),
            Write(@"Addon\Addon-Forever.toc", "Forever.lua\n"),
            Write(@"Addon\Bindings.xml", "<Bindings><Script file=\"NotFollowed.lua\"/></Bindings>"),
            Write(@"Addon\Core.lua", "local a = 1"),
            Write(@"Addon\locale.lua", "L = {}"),
            Write(@"Addon\Vanilla.lua", "vanilla"),
            Write(@"Addon\Forever.lua", "forever"),
            Write(@"Addon\ui\Frames.xml", "<Ui>\n<!-- <Script file=\"Commented.lua\"/> -->\n<Script file=\"Frames.lua\"/>\n<Include file='sub/Inner.xml' />\n</Ui>"),
            Write(@"Addon\ui\Frames.lua", "frames"),
            Write(@"Addon\ui\sub\Inner.xml", "<Ui><Script file=\"Inner.lua\"/></Ui>"),
            Write(@"Addon\ui\sub\Inner.lua", "inner"),
        ];
        Write(@"Addon\Addon_Camelot.toc", "Camelot.lua\n");
        Write(@"Addon\Camelot.lua", "camelot");
        Write(@"Addon\NotFollowed.lua", "bindings includes are not followed");
        Write(@"Addon\Unlisted.lua", "unlisted");
        Write(@"Addon\ui\Commented.lua", "commented");
        Write(@"Addon\libs\Lib\Lib.toc", "Lib.lua\n");
        Write(@"Addon\libs\Lib\Lib.lua", "embedded");
        Write(@"Other\Evil.lua", "outside");

        Assert.Equal(Expected(included), CurseForgeFingerprint.Compute(Path.Combine(_addOnsPath, "Addon")));
    }

    [Fact]
    public void Compute_IgnoresWhitespaceOnlyChanges()
    {
        Write(@"A\A.toc", "Core.lua\n");
        Write(@"A\Core.lua", "local a = 1");
        var before = CurseForgeFingerprint.Compute(Path.Combine(_addOnsPath, "A"));

        Write(@"A\Core.lua", "local  a =\r\n\t1 ");

        Assert.Equal(before, CurseForgeFingerprint.Compute(Path.Combine(_addOnsPath, "A")));
    }

    [Fact]
    public void Compute_ChangesWhenAnIncludedFileChanges()
    {
        Write(@"A\A.toc", "Core.lua\n");
        Write(@"A\Core.lua", "local a = 1");
        var before = CurseForgeFingerprint.Compute(Path.Combine(_addOnsPath, "A"));

        Write(@"A\Core.lua", "local a = 2");

        Assert.NotEqual(before, CurseForgeFingerprint.Compute(Path.Combine(_addOnsPath, "A")));
    }
}
