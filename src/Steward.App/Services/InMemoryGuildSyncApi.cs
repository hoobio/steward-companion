using Steward.Core;

namespace Steward.App.Services;

public enum SyncScenario
{
    InSync,
    Ready,
    DatasetFailure,
    Unreachable,
    Slow,
}

public sealed class InMemoryGuildSyncApi : IGuildSyncApi
{
    public const string RosterDataset = "roster";
    public const string LootDataset = "loot";
    public const string AttendanceDataset = "attendance";

    public const string DatasetFailureMessage =
        "The server rejected this payload: 3 loot events reference an unknown character. Nothing was recorded.";

    private const string SampleAccountFile = @"WTF\Account\54939295#1\SavedVariables\Steward.lua";
    private const string SampleCharacterFile =
        @"WTF\Account\54939295#1\Nightslayer\Hoobi\SavedVariables\Steward.lua";

    private static readonly TimeSpan SlowStep = TimeSpan.FromMilliseconds(250);
    private static readonly DateTimeOffset SampleExportedAt = DateTimeOffset.Now.AddMinutes(-18);

    private static readonly Dictionary<string, int> Behind = new(StringComparer.Ordinal)
    {
        [RosterDataset] = 4,
        [LootDataset] = 137,
        [AttendanceDataset] = 3,
    };

    public SyncScenario Scenario { get; set; } = SyncScenario.Ready;

    public static SavedVariablesSnapshot LocalSample { get; } = BuildSample();

    public Task<SyncServerState> GetStateAsync(CancellationToken ct)
    {
        ThrowIfUnreachable();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [RosterDataset] = ServerCount(RosterDataset, LocalSample.Roster.Count),
            [LootDataset] = ServerCount(LootDataset, LocalSample.Loot.Count),
            [AttendanceDataset] = ServerCount(AttendanceDataset, LocalSample.Attendance.Count),
        };

        var lastSynced = Scenario == SyncScenario.InSync
            ? DateTimeOffset.Now.AddMinutes(-4)
            : DateTimeOffset.Now.AddHours(-2);

        return Task.FromResult(new SyncServerState(counts, $"cursor-{Scenario}", lastSynced));
    }

    public async Task<SyncPushResult> PushAsync(
        string dataset,
        SyncPayload payload,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ThrowIfUnreachable();

        if (Scenario == SyncScenario.Slow)
        {
            for (var step = 1; step <= 12; step++)
            {
                await Task.Delay(SlowStep, ct).ConfigureAwait(true);
                progress?.Report(step / 12.0);
            }
        }
        else
        {
            progress?.Report(1);
        }

        if (Scenario == SyncScenario.DatasetFailure && string.Equals(dataset, LootDataset, StringComparison.Ordinal))
        {
            return new SyncPushResult(false, 0, DatasetFailureMessage);
        }

        return new SyncPushResult(true, CountFor(payload, dataset), null);
    }

    public Task<SyncPayload> PullAsync(CancellationToken ct)
    {
        ThrowIfUnreachable();

        return Task.FromResult(new SyncPayload(
            DateTimeOffset.Now,
            LocalSample.ExportedAt,
            LocalSample.Roster,
            LocalSample.Loot,
            LocalSample.Attendance));
    }

    private static int CountFor(SyncPayload payload, string dataset) => dataset switch
    {
        RosterDataset => payload.Roster.Count,
        LootDataset => payload.Loot.Count,
        _ => payload.Attendance.Count,
    };

    private static SavedVariablesSnapshot BuildSample()
    {
        var roster = Enumerable.Range(1, 38)
            .Select(i => new RosterMember(
                $"Guildie{i:00}",
                "Nightslayer",
                "WARRIOR",
                60,
                i <= 3 ? "Officer" : "Raider",
                i <= 3 ? 1 : 3,
                string.Empty,
                string.Empty,
                SampleExportedAt.AddHours(-i)))
            .ToList();

        var loot = Enumerable.Range(1, 812)
            .Select(i => new LootEvent(
                $"loot-{i:0000}",
                SampleExportedAt.AddMinutes(-i),
                $"Guildie{(i % 38) + 1:00}",
                19019 + i,
                "Thunderfury",
                5,
                "Ragnaros",
                "Molten Core"))
            .ToList();

        var attendance = Enumerable.Range(1, 46)
            .Select(i => new AttendanceRecord(
                $"raid-{i:000}",
                SampleExportedAt.AddDays(-i),
                "Molten Core",
                ["Hoobi", "Grug"]))
            .ToList();

        var files = new List<SavedVariablesFile>
        {
            new(SampleAccountFile, SampleExportedAt, SampleExportedAt, null),
            new(SampleCharacterFile, SampleExportedAt, SampleExportedAt, "Hoobi"),
        };

        return new SavedVariablesSnapshot(files, SampleExportedAt, roster, loot, attendance, 0);
    }

    private int ServerCount(string dataset, int localCount) =>
        Scenario == SyncScenario.InSync ? localCount : Math.Max(0, localCount - Behind[dataset]);

    private void ThrowIfUnreachable()
    {
        if (Scenario == SyncScenario.Unreachable)
        {
            throw new HttpRequestException("No such host is known. (api.hoobi.io:443)");
        }
    }
}
