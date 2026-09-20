namespace Steward.Core.Tests;

public sealed class AdminMeTests
{
    private static AdminMe Me(params string[] guildIds) => new(
        new AdminUser("1", "Hoobi", "hoobi", null, "global"),
        [.. guildIds.Select(id => new AdminGuild(id, $"Guild {id}", null, 10, null))]);

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
}
