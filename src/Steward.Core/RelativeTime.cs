namespace Steward.Core;

public static class RelativeTime
{
    public static string Describe(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        var days = (int)elapsed.TotalDays;
        return true switch
        {
            _ when elapsed < TimeSpan.FromHours(1) => "just now",
            _ when days < 1 => $"{Plural((int)elapsed.TotalHours, "hour")} ago",
            _ when days == 1 => "yesterday",
            _ when days < 30 => $"{Plural(days, "day")} ago",
            _ when days < 365 => $"{Plural(days / 30, "month")} ago",
            _ => $"{Plural(days / 365, "year")} ago",
        };
    }

    public static string DescribeUntil(DateTimeOffset at, DateTimeOffset now)
    {
        var remaining = at - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "any moment";
        }

        var minutes = Math.Max(1, (int)Math.Round(remaining.TotalMinutes, MidpointRounding.AwayFromZero));
        return minutes < 60
            ? $"in {Plural(minutes, "minute")}"
            : $"in {Plural((int)Math.Round(remaining.TotalHours, MidpointRounding.AwayFromZero), "hour")}";
    }

    private static string Plural(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")}";
}
