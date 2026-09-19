namespace Steward.Core;

public enum Freshness
{
    NoData,
    Fresh,
    Stale,
}

public static class SavedVariablesFreshness
{
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(2);

    public static Freshness Judge(SavedVariablesSnapshot? snapshot, WowClientProcess? client)
    {
        var lastFileWrite = NewestFileWriteTime(snapshot);
        if (lastFileWrite is null)
        {
            return Freshness.NoData;
        }

        if (client is null)
        {
            return Freshness.Fresh;
        }

        // Clock granularity: require the write to clearly postdate the start, not just edge past it.
        return lastFileWrite.Value > client.StartTime + ClockTolerance ? Freshness.Fresh : Freshness.Stale;
    }

    public static DateTimeOffset? LastWrite(SavedVariablesSnapshot? snapshot) =>
        snapshot?.ExportedAt ?? NewestFileWriteTime(snapshot);

    private static DateTimeOffset? NewestFileWriteTime(SavedVariablesSnapshot? snapshot) =>
        snapshot is { Files.Count: > 0 } ? snapshot.Files.Max(file => file.LastWriteTime) : null;
}
