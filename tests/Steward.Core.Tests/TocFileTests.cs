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

    [Theory]
    [InlineData("1.60.1.70009", 16001)]
    [InlineData("5.5.1.63538", 50501)]
    [InlineData("1.60.1", 16001)]
    [InlineData(null, null)]
    [InlineData("garbage", null)]
    public void InterfaceNumber_ParsesMajorMinorPatch(string? clientVersion, int? expected)
    {
        Assert.Equal(expected, TocFile.InterfaceNumber(clientVersion));
    }

    [Theory]
    [InlineData("16001", 16001, true)]
    [InlineData("11509, 50504, 120100, 16001", 16001, true)]
    [InlineData("11509, 50504", 16001, false)]
    [InlineData(null, 16001, false)]
    public void MatchesInterface_ReadsCommaSeparatedList(string? directive, int clientInterface, bool expected)
    {
        Assert.Equal(expected, TocFile.MatchesInterface(directive, clientInterface));
    }

    [Theory]
    [InlineData(16001, "1.60.1")]
    [InlineData(50500, "5.5.0")]
    public void FormatInterface_FormatsAsDottedVersion(int value, string expected)
    {
        Assert.Equal(expected, TocFile.FormatInterface(value));
    }
}
