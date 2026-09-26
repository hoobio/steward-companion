namespace Steward.Core.Tests;

public sealed class AdminMeTests
{
    private static AdminMe Me(params string[] guildIds) => new(
        new AdminUser("1", "Hoobi", "hoobi", null, "global"),
        [.. guildIds.Select(id => new AdminGuild(id, $"Guild {id}", null, 10, null))]);

    private static AdminMe MeWithRole(string? role, string[]? features = null) => new(
        new AdminUser("1", "Hoobi", "hoobi", null, role, features),
        []);

    [Fact]
    public void ResolveGuild_NoStoredId_TakesTheFirstGuild()
    {
        Assert.Equal("a", Me("a", "b").ResolveGuild(null)?.Id);
    }

    [Fact]
    public void ResolveGuild_StoredIdPresent_TakesThatGuild()
    {
        Assert.Equal("b", Me("a", "b").ResolveGuild("b")?.Id);
    }

    [Fact]
    public void ResolveGuild_StoredIdAbsent_FallsBackToTheFirstGuild()
    {
        Assert.Equal("a", Me("a", "b").ResolveGuild("gone")?.Id);
    }

    [Fact]
    public void ResolveGuild_NoGuilds_IsNull()
    {
        Assert.Null(Me().ResolveGuild("a"));
    }

    [Fact]
    public void EffectiveFeatures_FeaturesPresent_UsesThemVerbatim()
    {
        var features = GigagrugClient.EffectiveFeatures(MeWithRole(null, ["steward"]));

        Assert.Equal(["steward"], features);
    }

    [Fact]
    public void EffectiveFeatures_FeaturesAbsent_OfficerFallsBackToAllThree()
    {
        var features = GigagrugClient.EffectiveFeatures(MeWithRole("admin"));

        Assert.Equal(
            new HashSet<string> { "addons", "guides", "steward" },
            features);
    }

    [Fact]
    public void EffectiveFeatures_FeaturesAbsent_NonOfficerFallsBackToNone()
    {
        var features = GigagrugClient.EffectiveFeatures(MeWithRole(null));

        Assert.Empty(features);
    }

    [Fact]
    public void EffectiveFeatures_EmptyFeaturesArray_StaysEmptyEvenForAnOfficer()
    {
        var features = GigagrugClient.EffectiveFeatures(MeWithRole("global", []));

        Assert.Empty(features);
    }
}
