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
    [InlineData(new[] { "stable", "beta", "unstable" }, "stable")]
    [InlineData(new[] { "beta", "unstable" }, "beta")]
    [InlineData(new[] { "unstable" }, "unstable")]
    public void Resolve_NothingStored_PrefersStable_ThenBeta_ThenUnstable(string[] available, string expected)
    {
        var status = AddonChannelStatus.Resolve(null, ReleasesOn(available));

        Assert.Equal(expected, status.Channel);
    }

    [Fact]
    public void Resolve_StoredChannelWithARelease_KeepsIt_AndGivesNoNotice()
    {
        var status = AddonChannelStatus.Resolve("beta", ReleasesOn("stable", "beta", "unstable"));

        Assert.Equal("beta", status.Channel);
        Assert.Null(status.Notice);
    }

    [Fact]
    public void Resolve_StoredChannelWentStale_FallsBackAndSaysSo()
    {
        var status = AddonChannelStatus.Resolve("beta", ReleasesOn("stable"));

        Assert.Equal("stable", status.Channel);
        Assert.Equal("No releases on beta any more. Showing stable.", status.Notice);
    }

    [Fact]
    public void Resolve_NoReleasesAnywhere_ReturnsNullChannelAndNoNotice()
    {
        var status = AddonChannelStatus.Resolve(null, ReleasesOn());

        Assert.Null(status.Channel);
        Assert.Null(status.Notice);
    }

    [Fact]
    public void Resolve_StoredChannelIsNotVisible_TreatsItAsStale()
    {
        var releases = new Dictionary<string, AddonRelease?>
        {
            ["stable"] = Release,
            ["beta"] = Release,
        };

        var status = AddonChannelStatus.Resolve("unstable", releases);

        Assert.Equal("stable", status.Channel);
        Assert.Equal("No releases on unstable any more. Showing stable.", status.Notice);
    }

    [Fact]
    public void Has_ReportsPerChannelAvailability()
    {
        var status = AddonChannelStatus.Resolve("stable", ReleasesOn("stable", "unstable"));

        Assert.True(status.Has("stable"));
        Assert.False(status.Has("beta"));
        Assert.True(status.Has("unstable"));
    }
}
