namespace Steward.Core.Tests;

public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(1, "1 hour ago")]
    [InlineData(2, "2 hours ago")]
    [InlineData(26, "yesterday")]
    [InlineData(6 * 24, "6 days ago")]
    [InlineData(45 * 24, "1 month ago")]
    [InlineData(400 * 24, "1 year ago")]
    public void Describe_ReturnsExpectedText(double hoursAgo, string expected)
    {
        var at = Now - TimeSpan.FromHours(hoursAgo);

        Assert.Equal(expected, RelativeTime.Describe(at, Now));
    }
}
