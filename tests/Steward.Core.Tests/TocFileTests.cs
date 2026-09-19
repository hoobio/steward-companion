namespace Steward.Core.Tests;

public sealed class TocFileTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("steward-toc-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteToc(string content)
    {
        var path = Path.Combine(_tempDir, "HoobiScripts.toc");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadVersion_ReturnsValue_WhenPresent()
    {
        var path = WriteToc("## Interface: 11507\n## Version: 1.2.3\n");
        Assert.Equal("1.2.3", TocFile.ReadVersion(path));
    }

    [Fact]
    public void ReadVersion_ReturnsNull_WhenFileAbsent()
    {
        Assert.Null(TocFile.ReadVersion(Path.Combine(_tempDir, "missing.toc")));
    }

    [Fact]
    public void ReadVersion_ReturnsNull_WhenNoVersionLine()
    {
        var path = WriteToc("## Interface: 11507\n");
        Assert.Null(TocFile.ReadVersion(path));
    }

    [Fact]
    public void ReadVersion_ToleratesCrLf()
    {
        var path = WriteToc("## Interface: 11507\r\n## Version: 1.2.3\r\n");
        Assert.Equal("1.2.3", TocFile.ReadVersion(path));
    }

    [Fact]
    public void ReadVersion_TrimsExtraWhitespace()
    {
        var path = WriteToc("## Version:    1.2.3   \n");
        Assert.Equal("1.2.3", TocFile.ReadVersion(path));
    }

    [Theory]
    [InlineData("0.5.0", "0.5.0-beta.1", true)]
    [InlineData("0.5.0-beta.1", "0.5.0-beta.1", false)]
    [InlineData("0.5.0-beta.1", "0.5.0-unstable.219da29", true)]
    public void HasUpdate_IsPlainStringInequality_NotSemver(string available, string installed, bool expected)
    {
        Assert.Equal(expected, TocFile.HasUpdate(available, installed));
    }

    [Fact]
    public void HasUpdate_True_WhenInstalledIsNull()
    {
        Assert.True(TocFile.HasUpdate("1.0.0", null));
    }

    [Fact]
    public void HasUpdate_CanMoveEitherDirectionBetweenChannels()
    {
        Assert.True(TocFile.HasUpdate("0.4.0", "0.5.0-beta.1"));
        Assert.False(TocFile.HasUpdate("0.4.0", "0.4.0"));
    }
}
