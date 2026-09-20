namespace Steward.Core.Tests;

public sealed class SavedVariablesFreshnessTests
{
    private static readonly DateTimeOffset ClientStart = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static SavedVariablesSnapshot Snapshot(DateTimeOffset? exportedAt, params DateTimeOffset[] fileWriteTimes)
    {
        var files = fileWriteTimes
            .Select(time => new SavedVariablesFile("account.lua", time, exportedAt, null))
            .ToList();
        return new SavedVariablesSnapshot(files, exportedAt, [], [], [], 0);
    }

    [Fact]
    public void Judge_ReturnsNoData_WhenSnapshotIsNull()
    {
        Assert.Equal(Freshness.NoData, SavedVariablesFreshness.Judge(null, null));
    }

    [Fact]
    public void Judge_ReturnsNoData_WhenSnapshotHasNoFiles()
    {
        var snapshot = new SavedVariablesSnapshot([], null, [], [], [], 0);

        Assert.Equal(Freshness.NoData, SavedVariablesFreshness.Judge(snapshot, null));
    }

    [Fact]
    public void Judge_ReturnsFresh_WhenNoClientIsRunning()
    {
        var snapshot = Snapshot(null, ClientStart.AddHours(-1));

        Assert.Equal(Freshness.Fresh, SavedVariablesFreshness.Judge(snapshot, null));
    }

    [Fact]
    public void Judge_ReturnsStale_WhenNewestFileIsOlderThanClientStart()
    {
        var snapshot = Snapshot(null, ClientStart.AddHours(-1));
        var client = new WowClientProcess(1234, ClientStart);

        Assert.Equal(Freshness.Stale, SavedVariablesFreshness.Judge(snapshot, client));
    }

    [Fact]
    public void Judge_ReturnsFresh_WhenNewestFileIsNewerThanClientStart()
    {
        var snapshot = Snapshot(null, ClientStart.AddHours(1));
        var client = new WowClientProcess(1234, ClientStart);

        Assert.Equal(Freshness.Fresh, SavedVariablesFreshness.Judge(snapshot, client));
    }

    [Fact]
    public void Judge_ReturnsStale_WhenFileIsOneSecondBeforeStart_WithinTolerance()
    {
        var snapshot = Snapshot(null, ClientStart.AddSeconds(-1));
        var client = new WowClientProcess(1234, ClientStart);

        Assert.Equal(Freshness.Stale, SavedVariablesFreshness.Judge(snapshot, client));
    }

    [Fact]
    public void LastWrite_PrefersExportedAt()
    {
        var exportedAt = ClientStart.AddMinutes(-5);
        var snapshot = Snapshot(exportedAt, ClientStart.AddHours(-1));

        Assert.Equal(exportedAt, SavedVariablesFreshness.LastWrite(snapshot));
    }

    [Fact]
    public void LastWrite_FallsBackToNewestFileWriteTime_WhenExportedAtMissing()
    {
        var oldest = ClientStart.AddHours(-2);
        var newest = ClientStart.AddHours(-1);
        var snapshot = new SavedVariablesSnapshot(
            [
                new SavedVariablesFile("a.lua", oldest, null, null),
                new SavedVariablesFile("b.lua", newest, null, null),
            ],
            null,
            [],
            [],
            [],
            0);

        Assert.Equal(newest, SavedVariablesFreshness.LastWrite(snapshot));
    }

    [Fact]
    public void LastWrite_ReturnsNull_WhenNoFiles()
    {
        var snapshot = new SavedVariablesSnapshot([], null, [], [], [], 0);

        Assert.Null(SavedVariablesFreshness.LastWrite(snapshot));
    }

    [Fact]
    public void LastWrite_ReturnsNull_WhenSnapshotIsNull()
    {
        Assert.Null(SavedVariablesFreshness.LastWrite(null));
    }

    [Fact]
    public void WrittenSince_TracksNewestSavedVariablesFile()
    {
        var flavourPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var savedVariables = Path.Combine(flavourPath, "WTF", "Account", "ACCOUNT#1", "SavedVariables");
        Directory.CreateDirectory(savedVariables);
        var file = Path.Combine(savedVariables, "Steward.lua");
        File.WriteAllText(file, "StewardDB = {}");
        var writtenAt = (DateTimeOffset)File.GetLastWriteTimeUtc(file);
        try
        {
            Assert.False(SavedVariablesFreshness.WrittenSince(Path.Combine(flavourPath, "missing"), writtenAt.AddMinutes(-1)));
            Assert.False(SavedVariablesFreshness.WrittenSince(flavourPath, writtenAt));
            Assert.True(SavedVariablesFreshness.WrittenSince(flavourPath, writtenAt.AddMinutes(-1)));
        }
        finally
        {
            Directory.Delete(flavourPath, recursive: true);
        }
    }
}
