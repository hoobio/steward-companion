using Microsoft.Extensions.Logging;

using Steward.Core.Diagnostics;

namespace Steward.Core.Tests;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-file-logger-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void CreateLogger_WritesTimestampLevelCategoryAndMessage()
    {
        var now = new DateTimeOffset(2026, 9, 27, 12, 34, 56, TimeSpan.Zero);
        using var provider = new FileLoggerProvider(_root, () => now);
        var logger = provider.CreateLogger("Steward.App.ViewModels.MainViewModel");

        logger.Warn(null, "Sign-in failed");
        provider.Dispose();

        var line = File.ReadAllText(Path.Combine(_root, "steward-20260927.log"));
        Assert.Contains("2026-09-27T12:34:56", line);
        Assert.Contains("WRN", line);
        Assert.Contains("Steward.App.ViewModels.MainViewModel", line);
        Assert.Contains("Sign-in failed", line);
    }

    [Fact]
    public void CreateLogger_IncludesTheExceptionStackTrace()
    {
        using var provider = new FileLoggerProvider(_root, () => DateTimeOffset.UtcNow);
        var logger = provider.CreateLogger("Test");

        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException ex)
        {
            logger.Err(ex, "Failed");
        }

        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_root, "*.log").Single());
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void Write_RollsToANewFile_WhenTheUtcDateChanges()
    {
        var current = new DateTimeOffset(2026, 9, 27, 23, 59, 0, TimeSpan.Zero);
        using var provider = new FileLoggerProvider(_root, () => current);
        var logger = provider.CreateLogger("Test");

        logger.Info("day one");
        current = current.AddMinutes(2);
        logger.Info("day two");
        provider.Dispose();

        Assert.True(File.Exists(Path.Combine(_root, "steward-20260927.log")));
        Assert.True(File.Exists(Path.Combine(_root, "steward-20260928.log")));
    }

    [Fact]
    public void Constructor_DeletesLogFilesOlderThanRetention()
    {
        var stale = Path.Combine(_root, "steward-20260101.log");
        File.WriteAllText(stale, "stale");

        using var provider = new FileLoggerProvider(_root, () => new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));

        Assert.False(File.Exists(stale));
    }
}
