namespace Steward.Core;

public sealed record ScheduleEvent(string Name, DateTimeOffset Start, DateTimeOffset? End, bool Headline = false, string? UpcomingText = null, string? ActiveText = null);

public static class ForeverSchedule
{
    // Blizzard's Americas and Oceania times (news.blizzard.com, read 21 Oct 2026); the stress test has no stated end, and name reservations give only the date 3 Nov, so they end at the end of that day Pacific.
    public static readonly IReadOnlyList<ScheduleEvent> Events =
    [
        new("Stress test", new(2026, 10, 21, 14, 0, 0, TimeSpan.FromHours(-7)), null),
        new("Name reservations", new(2026, 10, 27, 13, 0, 0, TimeSpan.FromHours(-7)), new(2026, 11, 3, 23, 59, 0, TimeSpan.FromHours(-8)), ActiveText: "Live now"),
        new("Launch", new(2026, 11, 4, 15, 0, 0, TimeSpan.FromHours(-8)), null, Headline: true),
    ];

    public static ScheduleEvent Launch => Events[^1];

    public static readonly TimeSpan LiveFor = TimeSpan.FromDays(1);

    public static string? TitleCountdown(IReadOnlyList<ScheduleEvent> events, DateTimeOffset now) =>
        events.FirstOrDefault(schedule => schedule.Headline) is not { } headline ? null
        : now < headline.Start ? Humanize(headline.Start - now)
        : now < (headline.End ?? headline.Start + LiveFor) ? "Live now"
        : null;

    public static IEnumerable<ScheduleEvent> Current(IReadOnlyList<ScheduleEvent> events, DateTimeOffset now) =>
        events.Where(schedule => now < (schedule.End ?? schedule.Start));

    public static string Status(ScheduleEvent schedule, DateTimeOffset now) =>
        now < schedule.Start || schedule.End is not { } end
            ? Fill(schedule.UpcomingText ?? "Starts in {time}", schedule.Start - now)
            : Fill(schedule.ActiveText ?? "Ends in {time}", end - now);

    private static string Fill(string text, TimeSpan span) => text.Replace("{time}", Humanize(span), StringComparison.Ordinal);

    public static string Humanize(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return "under a minute";
        }

        var minutes = (int)span.TotalMinutes;
        var (days, hours, rest) = (minutes / 1440, minutes / 60 % 24, minutes % 60);
        return days > 0 ? Join(Unit(days, "day"), hours > 0 ? Unit(hours, "hour") : null)
            : hours > 0 ? Join(Unit(hours, "hour"), rest > 0 ? Unit(rest, "minute") : null)
            : Unit(rest, "minute");
    }

    private static string Unit(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")}";

    private static string Join(string first, string? second) => second is null ? first : $"{first} {second}";
}
