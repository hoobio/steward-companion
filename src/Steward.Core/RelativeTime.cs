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
            _ when days < 1 => Plural((int)elapsed.TotalHours, "hour"),
            _ when days == 1 => "yesterday",
            _ when days < 30 => Plural(days, "day"),
            _ when days < 365 => Plural(days / 30, "month"),
            _ => Plural(days / 365, "year"),
        };
    }

    private static string Plural(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")} ago";
}
