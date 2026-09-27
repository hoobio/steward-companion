namespace Steward.Core.Tests;

public sealed class BannerTests
{
    private static Banner Sample(
        string id = "b1",
        int revision = 1,
        string? minVersion = null,
        string? maxVersion = null,
        IReadOnlyList<string>? channels = null) =>
        new(id, revision, "info", "Title", "Message", true, minVersion, maxVersion, channels);

    [Fact]
    public void BannerLevels_Parse_MapsKnownLevels()
    {
        Assert.Equal(BannerLevel.Success, BannerLevels.Parse("success"));
        Assert.Equal(BannerLevel.Warning, BannerLevels.Parse("warning"));
        Assert.Equal(BannerLevel.Error, BannerLevels.Parse("error"));
        Assert.Equal(BannerLevel.Info, BannerLevels.Parse("info"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("critical")]
    public void BannerLevels_Parse_UnknownOrMissingFallsBackToInfo(string? level)
    {
        Assert.Equal(BannerLevel.Info, BannerLevels.Parse(level));
    }

    [Theory]
    [InlineData("store_update", BannerActionKind.StoreUpdate)]
    [InlineData("open_url", BannerActionKind.OpenUrl)]
    [InlineData("navigate", BannerActionKind.Navigate)]
    [InlineData("dismiss", BannerActionKind.Dismiss)]
    public void BannerActions_Parse_MapsKnownTypes(string type, BannerActionKind expected)
    {
        Assert.Equal(expected, BannerActions.Parse(type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("reboot_machine")]
    public void BannerActions_Parse_UnknownOrMissingFallsBackToUnknown(string? type)
    {
        Assert.Equal(BannerActionKind.Unknown, BannerActions.Parse(type));
    }

    [Theory]
    [InlineData("https://hoobi.io", true)]
    [InlineData("ms-windows-store://pdp/?productid=1", true)]
    [InlineData("http://hoobi.io", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    [InlineData("not a url", false)]
    public void BannerActions_IsAllowedUrl_OnlyAllowsHttpsAndStoreLinks(string? url, bool expected)
    {
        Assert.Equal(expected, BannerActions.IsAllowedUrl(url));
    }

    [Fact]
    public void BannerParsing_Parse_ReadsAValidBanner()
    {
        var json = """
            {"banners":[{"id":"b1","revision":3,"level":"error","title":"Update required",
            "message":"You must update","dismissible":false,"min_version":null,"max_version":"0.12.99",
            "channels":["store"],"actions":[{"label":"Update","type":"store_update"}]}]}
            """;

        var banners = BannerParsing.Parse(json);

        var banner = Assert.Single(banners);
        Assert.Equal("b1", banner.Id);
        Assert.Equal(3, banner.Revision);
        Assert.Equal("error", banner.Level);
        Assert.False(banner.Dismissible);
        Assert.Equal("0.12.99", banner.MaxVersion);
        Assert.Equal("store", Assert.Single(banner.Channels!));
        Assert.Equal("store_update", Assert.Single(banner.Actions!).Type);
    }

    [Fact]
    public void BannerParsing_Parse_IgnoresUnknownFields()
    {
        var json = """
            {"banners":[{"id":"b1","revision":1,"level":"info","title":"T","message":"M",
            "future_field":"anything","nested":{"a":1}}]}
            """;

        var banner = Assert.Single(BannerParsing.Parse(json));
        Assert.Equal("b1", banner.Id);
    }

    [Fact]
    public void BannerParsing_Parse_SkipsAMalformedBannerButKeepsTheOthers()
    {
        var json = """
            {"banners":[
                {"id":"good-1","revision":1,"level":"info","title":"T","message":"M"},
                {"id":"bad","revision":"not-a-number","level":"info","title":"T","message":"M"},
                {"id":"good-2","revision":2,"level":"info","title":"T","message":"M"}
            ]}
            """;

        var banners = BannerParsing.Parse(json);

        Assert.Equal(["good-1", "good-2"], banners.Select(b => b.Id));
    }

    [Fact]
    public void BannerParsing_Parse_ReturnsEmpty_WhenBannersKeyIsMissing()
    {
        Assert.Empty(BannerParsing.Parse("{}"));
    }

    [Fact]
    public void BannerParsing_Parse_AcceptsAnIntegerIdAndNullActionFields()
    {
        var json = """
            {"banners":[{"id":7,"revision":2,"level":"warning","title":"Update available","message":"Steward 0.13.0 fixes guide sync.","dismissible":false,"min_version":"0.12.0","max_version":"0.12.9","channels":["msi","store"],"actions":[{"label":"Update","type":"store_update","url":null,"page":null},{"label":"Read more","type":"open_url","url":"https://hoobi.io/steward","page":null}]}]}
            """;

        var banner = Assert.Single(BannerParsing.Parse(json));

        Assert.Equal("7", banner.Id);
        Assert.Equal(2, banner.Actions!.Count);
        Assert.Equal("store_update", banner.Actions[0].Type);
        Assert.Equal("open_url", banner.Actions[1].Type);
        Assert.Equal("https://hoobi.io/steward", banner.Actions[1].Url);
    }

    [Fact]
    public void BannerParsing_Parse_MixedStringAndNumericIdsBothParse()
    {
        var json = """
            {"banners":[
                {"id":"string-id","revision":1,"level":"info","title":"T","message":"M"},
                {"id":42,"revision":1,"level":"info","title":"T","message":"M"}
            ]}
            """;

        var banners = BannerParsing.Parse(json);

        Assert.Equal(["string-id", "42"], banners.Select(b => b.Id));
    }

    [Fact]
    public void BannerFilter_Active_KeepsABannerWithNoVersionBounds()
    {
        var active = BannerFilter.Active([Sample()], "0.12.0", "store", new Dictionary<string, int>());

        Assert.Single(active);
    }

    [Theory]
    [InlineData("0.12.0", "0.13.0", null, false)]
    [InlineData("0.14.0", "0.13.0", null, true)]
    [InlineData("0.12.0", null, "0.13.0", true)]
    [InlineData("0.14.0", null, "0.13.0", false)]
    [InlineData("0.13.0", "0.13.0", "0.13.0", true)]
    public void BannerFilter_Active_ChecksVersionBoundsInclusively(
        string installed, string? min, string? max, bool expectedActive)
    {
        var banner = Sample(minVersion: min, maxVersion: max);

        var active = BannerFilter.Active([banner], installed, "store", new Dictionary<string, int>());

        Assert.Equal(expectedActive, active.Count == 1);
    }

    [Fact]
    public void BannerFilter_Active_ComparesFlightVersionsWithFourParts()
    {
        var banner = Sample(minVersion: "0.13.40.0", maxVersion: "0.13.99.0");

        Assert.Single(BannerFilter.Active([banner], "0.13.42.0", "store", new Dictionary<string, int>()));
        Assert.Empty(BannerFilter.Active([banner], "0.13.39.9", "store", new Dictionary<string, int>()));
    }

    [Fact]
    public void BannerFilter_Active_TreatsMissingVersionPartsAsZero()
    {
        var banner = Sample(minVersion: "0.13", maxVersion: "0.13.0.5");

        Assert.Single(BannerFilter.Active([banner], "0.13.0.0", "store", new Dictionary<string, int>()));
    }

    [Fact]
    public void BannerFilter_Active_EmptyChannelsMeansEveryChannel()
    {
        var banner = Sample(channels: []);

        Assert.Single(BannerFilter.Active([banner], "0.12.0", "dev", new Dictionary<string, int>()));
        Assert.Single(BannerFilter.Active([banner], "0.12.0", "msi", new Dictionary<string, int>()));
    }

    [Fact]
    public void BannerFilter_Active_DropsABannerNotInTheRunningChannel()
    {
        var banner = Sample(channels: ["store"]);

        Assert.Empty(BannerFilter.Active([banner], "0.12.0", "dev", new Dictionary<string, int>()));
        Assert.Single(BannerFilter.Active([banner], "0.12.0", "store", new Dictionary<string, int>()));
    }

    [Fact]
    public void BannerFilter_Active_DropsABannerDismissedAtTheSameRevision()
    {
        var banner = Sample(id: "b1", revision: 3);
        var dismissed = new Dictionary<string, int> { ["b1"] = 3 };

        Assert.Empty(BannerFilter.Active([banner], "0.12.0", "store", dismissed));
    }

    [Fact]
    public void BannerFilter_Active_KeepsABannerDismissedAtAnOlderRevision()
    {
        var banner = Sample(id: "b1", revision: 4);
        var dismissed = new Dictionary<string, int> { ["b1"] = 3 };

        Assert.Single(BannerFilter.Active([banner], "0.12.0", "store", dismissed));
    }
}
