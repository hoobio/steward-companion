using Steward.Core;

namespace Steward.Core.Tests;

public sealed class ForeverScheduleTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 18, 0, 0, TimeSpan.Zero);
    private static readonly ScheduleEvent Window = new("Name reservations", Start, Start.AddDays(7));

    [Theory]
    [InlineData(30, "under a minute")]
    [InlineData(60, "1 minute")]
    [InlineData(59 * 60, "59 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(2 * 3600 + 5 * 60, "2 hours 5 minutes")]
    [InlineData(86400, "1 day")]
    [InlineData(4 * 86400 + 6 * 3600 + 59 * 60, "4 days 6 hours")]
    public void Humanize_shows_the_two_largest_units(int seconds, string expected) =>
        Assert.Equal(expected, ForeverSchedule.Humanize(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Status_counts_to_the_start_then_the_end()
    {
        Assert.Equal("Starts in 1 day", ForeverSchedule.Status(Window, Start.AddDays(-1)));
        Assert.Equal("Ends in 6 days", ForeverSchedule.Status(Window, Start.AddDays(1)));
    }

    [Fact]
    public void An_event_leaves_the_list_once_it_starts_with_no_end_or_once_it_ends()
    {
        var stressTest = ForeverSchedule.Events[0];
        var reservations = ForeverSchedule.Events[1];
        Assert.Contains(stressTest, ForeverSchedule.Current(stressTest.Start.AddMinutes(-1)));
        Assert.DoesNotContain(stressTest, ForeverSchedule.Current(stressTest.Start));
        Assert.Contains(reservations, ForeverSchedule.Current(reservations.Start.AddDays(1)));
        Assert.DoesNotContain(reservations, ForeverSchedule.Current(reservations.End!.Value));
    }

    [Fact]
    public void The_title_countdown_reads_live_now_for_a_day_after_launch()
    {
        var launch = ForeverSchedule.Launch.Start;
        Assert.Equal("2 hours", ForeverSchedule.TitleCountdown(launch.AddHours(-2)));
        Assert.Equal("Live now", ForeverSchedule.TitleCountdown(launch));
        Assert.Equal("Live now", ForeverSchedule.TitleCountdown(launch.AddHours(23)));
        Assert.Null(ForeverSchedule.TitleCountdown(launch.AddDays(1)));
    }

    [Fact]
    public void Events_are_in_start_order_and_end_with_the_launch() =>
        Assert.Equal(ForeverSchedule.Events.OrderBy(schedule => schedule.Start), ForeverSchedule.Events);
}
