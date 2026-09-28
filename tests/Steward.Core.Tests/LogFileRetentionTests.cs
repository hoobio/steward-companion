using Steward.Core.Diagnostics;

namespace Steward.Core.Tests;

public sealed class LogFileRetentionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-log-retention-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void DeleteOlderThan_RemovesFilesOlderThanRetention_KeepsNewerAndUnrelatedFiles()
    {
        var now = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
        var old = Path.Combine(_root, "steward-store-20260901.log");
        var recent = Path.Combine(_root, "steward-dev-20260926.log");
        var unrelated = Path.Combine(_root, "update.log");
        File.WriteAllText(old, "old");
        File.WriteAllText(recent, "recent");
        File.WriteAllText(unrelated, "unrelated");

        LogFileRetention.DeleteOlderThan(_root, retentionDays: 14, now);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void DeleteOlderThan_DoesNothing_WhenDirectoryIsMissing() =>
        LogFileRetention.DeleteOlderThan(Path.Combine(_root, "missing"), retentionDays: 14, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("steward-store-20260927.log", true)]
    [InlineData("steward-20260927.log", true)]
    [InlineData("update.log", false)]
    [InlineData("steward-notadate.log", false)]
    public void TryParseDate_OnlyMatchesTheOwnFileNameShape(string fileName, bool expected) =>
        Assert.Equal(expected, LogFileRetention.TryParseDate(fileName, out _));

    [Fact]
    public void FileNameFor_IncludesTheBuildName() =>
        Assert.Equal("steward-store-20260927.log", LogFileRetention.FileNameFor("store", new DateOnly(2026, 9, 27)));
}
