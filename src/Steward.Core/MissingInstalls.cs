namespace Steward.Core;

public sealed record MissingInstallReport(AppState State, IReadOnlyList<string> Missing, IReadOnlyList<string> DueForRemoval);

public static class MissingInstalls
{
    public static readonly TimeSpan RemovalDelay = TimeSpan.FromDays(14);

    public static MissingInstallReport Detect(
        AppState state,
        DateTimeOffset now,
        Func<string, bool> directoryExists,
        Func<string, bool> driveRootExists)
    {
        var missingSince = new Dictionary<string, DateTimeOffset>(state.MissingSince ?? [], StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var due = new List<string>();

        foreach (var path in state.AddedInstalls ?? [])
        {
            if (directoryExists(path))
            {
                missingSince.Remove(path);
                continue;
            }

            missing.Add(path);
            var since = missingSince.TryGetValue(path, out var recorded) ? recorded : missingSince[path] = now;
            if (now - since >= RemovalDelay && driveRootExists(path))
            {
                due.Add(path);
            }
        }

        var installs = new HashSet<string>(state.AddedInstalls ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var stale in missingSince.Keys.Where(key => !installs.Contains(key)).ToList())
        {
            missingSince.Remove(stale);
        }

        return new MissingInstallReport(state with { MissingSince = missingSince }, missing, due);
    }
}
