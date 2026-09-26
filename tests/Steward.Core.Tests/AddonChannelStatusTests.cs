namespace Steward.Core.Tests;

public sealed class AddonChannelStatusTests
{
    private static readonly AddonRelease Release =
        new("1.0.0", "Addon-1.0.0.zip", "abc123", 100, DateTimeOffset.UtcNow);

    private static Dictionary<string, AddonRelease?> ReleasesOn(params string[] channels)
    {
        var releases = new Dictionary<string, AddonRelease?>();
        foreach (var channel in AddonChannelStatus.Ordered)
        {
            releases[channel] = channels.Contains(channel) ? Release : null;
        }
        return releases;
    }

    [Theory]
    [InlineData(new[] { "release", "pre-release" }, "release")]
    [InlineData(new[] { "pre-release" }, "pre-release")]
    public void Resolve_NothingStored_PrefersRelease_ThenPreRelease(string[] available, string expected)
    {
        var status = AddonChannelStatus.Resolve(null, ReleasesOn(available), AddonChannelStatus.Ordered, AddonChannelStatus.DefaultPreference);

        Assert.Equal(expected, status.Channel);
    }

    [Fact]
    public void Resolve_StoredChannelWithARelease_KeepsIt_AndGivesNoNotice()
    {
        var status = AddonChannelStatus.Resolve("pre-release", ReleasesOn("release", "pre-release"), AddonChannelStatus.Ordered, AddonChannelStatus.DefaultPreference);

        Assert.Equal("pre-release", status.Channel);
        Assert.Null(status.Notice);
    }

    [Fact]
    public void Resolve_StoredChannelWentStale_FallsBackAndSaysSo()
    {
        var status = AddonChannelStatus.Resolve("pre-release", ReleasesOn("release"), AddonChannelStatus.Ordered, AddonChannelStatus.DefaultPreference);

        Assert.Equal("release", status.Channel);
        Assert.Equal("No releases on pre-release any more. Showing release.", status.Notice);
    }

    [Fact]
    public void Resolve_NoReleasesAnywhere_ReturnsNullChannelAndNoNotice()
    {
        var status = AddonChannelStatus.Resolve(null, ReleasesOn(), AddonChannelStatus.Ordered, AddonChannelStatus.DefaultPreference);

        Assert.Null(status.Channel);
        Assert.Null(status.Notice);
    }

    [Fact]
    public void Resolve_StoredChannelNoLongerExists_FallsBackWithoutNotice()
    {
        var releases = new Dictionary<string, AddonRelease?>
        {
            ["release"] = Release,
            ["pre-release"] = Release,
        };

        var status = AddonChannelStatus.Resolve("develop", releases, AddonChannelStatus.Ordered, AddonChannelStatus.DefaultPreference);

        Assert.Equal("release", status.Channel);
        Assert.Null(status.Notice);
    }

    [Fact]
    public void Has_ReportsPerChannelAvailability()
    {
        var status = AddonChannelStatus.Resolve("release", ReleasesOn("release"), AddonChannelStatus.Ordered, AddonChannelStatus.DefaultPreference);

        Assert.True(status.Has("release"));
        Assert.False(status.Has("pre-release"));
    }

    private static readonly IReadOnlyList<string> TwoChannels = ["release", "pre-release"];

    [Fact]
    public void Resolve_TwoChannelAddon_PrefersRelease()
    {
        var releaseOnly = new AddonRelease("v1.0.0", "z", "s", 1, DateTimeOffset.UtcNow.AddDays(-1));
        var prerelease = new AddonRelease("v1.1.0-rc1", "z", "s", 1, DateTimeOffset.UtcNow);
        var releases = new Dictionary<string, AddonRelease?>
        {
            ["release"] = releaseOnly,
            ["pre-release"] = prerelease,
        };

        var status = AddonChannelStatus.Resolve(null, releases, TwoChannels, TwoChannels);

        Assert.Equal("release", status.Channel);
    }

    [Fact]
    public void Resolve_NewerBuildOnMoreStableChannel_AddsNotice()
    {
        var older = new AddonRelease("v1.1.0-rc1", "z", "s", 1, DateTimeOffset.UtcNow.AddDays(-2));
        var newer = new AddonRelease("v1.1.0", "z", "s", 1, DateTimeOffset.UtcNow);
        var releases = new Dictionary<string, AddonRelease?>
        {
            ["release"] = newer,
            ["pre-release"] = older,
        };

        var status = AddonChannelStatus.Resolve("pre-release", releases, TwoChannels, TwoChannels);

        Assert.Equal("pre-release", status.Channel);
        Assert.Contains("release has a newer build", status.Notice);
    }

    [Fact]
    public void Resolve_ChosenChannelIsNewest_NoNotice()
    {
        var older = new AddonRelease("v1.0.0", "z", "s", 1, DateTimeOffset.UtcNow.AddDays(-2));
        var newer = new AddonRelease("v1.1.0-rc1", "z", "s", 1, DateTimeOffset.UtcNow);
        var releases = new Dictionary<string, AddonRelease?>
        {
            ["release"] = older,
            ["pre-release"] = newer,
        };

        var status = AddonChannelStatus.Resolve("pre-release", releases, TwoChannels, TwoChannels);

        Assert.Equal("pre-release", status.Channel);
        Assert.Null(status.Notice);
    }
}
