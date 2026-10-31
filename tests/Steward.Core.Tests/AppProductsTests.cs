using Steward.Core;

namespace Steward.Core.Tests;

public sealed class AppProductsTests
{
    private const string Body = """
        {"products":[
          {"code":"wowf","name":null,"icon_url":null,"extra":1,"events":[
            {"name":"Launch","starts_at":1762297200000,"ends_at":null,"headline":true},
            {"name":"Reservations","starts_at":1762000000000,"ends_at":1762100000000,"upcoming_text":"Opens in {time}","active_text":"Live now"},
            {"name":"","starts_at":1},
            {"name":"No start"},
            {"name":"Backwards","starts_at":10,"ends_at":5},
            "junk"
          ]},
          {"code":"wow","name":"Retail Override","icon_url":"https://cdn.test/wow.png","events":[]},
          {"code":"wow_classic","icon_url":"http://cdn.test/plain.png"},
          {"name":"No code"},
          42
        ]}
        """;

    [Fact]
    public void Parse_skips_malformed_entries_and_events()
    {
        var products = AppProducts.Parse(Body);

        Assert.Equal(["wowf", "wow", "wow_classic"], products.Select(product => product.Code));
        var wowf = products[0];
        Assert.Null(wowf.Name);
        Assert.Null(wowf.IconUrl);
        Assert.Equal(["Launch", "Reservations"], wowf.Events!.Select(entry => entry.Name));
        Assert.True(wowf.Events![0].Headline);
        Assert.Equal("Opens in {time}", wowf.Events[1].UpcomingText);
        Assert.Equal("Live now", wowf.Events[1].ActiveText);
        Assert.Equal("Retail Override", products[1].Name);
        Assert.Equal("https://cdn.test/wow.png", products[1].IconUrl);
        Assert.Empty(products[1].Events!);
        Assert.Null(products[2].IconUrl);
        Assert.Null(products[2].Events);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"products":null}""")]
    public void Parse_reads_no_products_from_a_body_without_a_list(string json) =>
        Assert.Empty(AppProducts.Parse(json));

    [Fact]
    public void Null_events_keep_the_bundled_schedule_and_empty_events_clear_it()
    {
        IReadOnlyList<ProductOverride> overrides =
        [
            new("wowf", Events: null),
            new("wow_classic_beta", Events: []),
            new("wow", Events: [new("Patch", 1762297200000, Headline: true)]),
        ];

        Assert.Same(ForeverSchedule.Events, AppProducts.Schedule("wowf", overrides));
        Assert.Empty(AppProducts.Schedule("wow_classic_beta", overrides));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1762297200000), Assert.Single(AppProducts.Schedule("wow", overrides)).Start);
        Assert.Same(ForeverSchedule.Events, AppProducts.Schedule("wow_classic_beta", []));
        Assert.Empty(AppProducts.Schedule("wow_classic_era", []));
        Assert.Empty(AppProducts.Schedule(null, overrides));
    }

    [Fact]
    public void Names_override_only_bundled_products()
    {
        var bundled = new Dictionary<string, string> { ["wowf"] = "World of Warcraft: Forever", ["wow"] = "World of Warcraft: Retail" };
        var names = AppProducts.Names(bundled, [new("wowf", "World of Warcraft: Forever!"), new("wow", null), new("wow_new", "New")]);

        Assert.Equal("World of Warcraft: Forever!", names["wowf"]);
        Assert.Equal("World of Warcraft: Retail", names["wow"]);
        Assert.False(names.ContainsKey("wow_new"));
    }

    [Fact]
    public void The_cached_list_round_trips_through_state() =>
        Assert.Equal(
            "Opens in {time}",
            System.Text.Json.JsonSerializer.Deserialize(
                System.Text.Json.JsonSerializer.Serialize([.. AppProducts.Parse(Body)], CompanionJsonContext.Default.ListProductOverride),
                CompanionJsonContext.Default.ListProductOverride)![0].Events![1].UpcomingText);
}
