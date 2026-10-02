namespace Steward.Core.Tests;

public sealed class CharacterPushRoutingTests
{
    private const string HomeGuid = "Player-1-00000001";
    private const string AltGuid = "Player-1-00000002";
    private const string ThirdGuid = "Player-1-00000003";

    private static CharacterObservation Character(string guid, string guild) =>
        new(guid, guid, "Nightslayer", guild, 60, 1, 2, 1, null, null, false, null);

    private static SavedVariablesSnapshot Snapshot(GuildRanks? ranks, params CharacterObservation[] characters) => new(
        [], null, [], [], [], 0,
        characters,
        "snapshot-fingerprint",
        new Dictionary<string, CharacterProfessions>(),
        new Dictionary<string, ProfessionCatalogue>(),
        ranks,
        HasAccountData: true);

    private static GuildRanks Ranks(string guild) =>
        new("Nightslayer", guild, null, new Dictionary<int, string> { [0] = "Guild Master" });

    private static IEnumerable<string> Guids(CharacterPushRoute route) => route.Scope.Characters.Select(c => c.CharacterGuid);

    [Fact]
    public void Route_SplitsCharactersAcrossTwoServers()
    {
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"), Character(AltGuid, "Other"), Character(ThirdGuid, "Nobody"));

        var routes = CharacterPushRouting.Route(
            snapshot,
            [new("a", false, ["Gigagrug"]), new("b", false, [" other "])],
            "a");

        Assert.Equal(["a", "b"], routes.Select(r => r.GuildId));
        Assert.Equal([HomeGuid], Guids(routes[0]));
        Assert.Equal([AltGuid], Guids(routes[1]));
    }

    [Fact]
    public void Route_SendsAWowGuildOnTwoListsToBoth()
    {
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"));

        var routes = CharacterPushRouting.Route(
            snapshot,
            [new("a", false, ["Gigagrug"]), new("b", false, ["GIGAGRUG", "Other"])],
            "a");

        Assert.Equal(2, routes.Count);
        Assert.All(routes, route => Assert.Equal([HomeGuid], Guids(route)));
    }

    [Fact]
    public void Route_NullListOnTheSelectedServer_SendsEverythingThereOnly()
    {
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"), Character(AltGuid, "Other"));

        var routes = CharacterPushRouting.Route(
            snapshot,
            [new("a", false, null), new("b", false, ["Gigagrug"])],
            "a");

        var route = Assert.Single(routes);
        Assert.Equal("a", route.GuildId);
        Assert.Equal([HomeGuid, AltGuid], Guids(route));
        Assert.Equal("snapshot-fingerprint", route.Scope.Fingerprint);
    }

    [Fact]
    public void Route_EmptyOrMissingLists_RouteNothing()
    {
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"));

        Assert.Empty(CharacterPushRouting.Route(snapshot, [new("a", false, []), new("b", false, null)], "a"));
        Assert.Empty(CharacterPushRouting.Route(snapshot, [new("a", false, ["Other"])], "a"));
        Assert.Empty(CharacterPushRouting.Route(snapshot, [], "a"));
    }

    [Fact]
    public void Route_UsesEachServersOwnMode()
    {
        var professions = new Dictionary<string, CharacterProfessions> { [HomeGuid] = new(null, null, null) };
        var snapshot = Snapshot(Ranks("Gigagrug"), Character(HomeGuid, "Gigagrug"), Character(AltGuid, "Gigagrug")) with { Professions = professions };

        var routes = CharacterPushRouting.Route(
            snapshot,
            [new("officer", false, ["Gigagrug"]), new("member", true, ["Gigagrug"])],
            "officer");

        Assert.False(routes[0].ProfessionsOnly);
        Assert.Equal([HomeGuid, AltGuid], Guids(routes[0]));
        Assert.True(routes[1].ProfessionsOnly);
        Assert.Equal([HomeGuid], Guids(routes[1]));
    }

    [Fact]
    public void Route_SendsGuildRanksOnlyToAnOfficerServerListingThatGuild()
    {
        var snapshot = Snapshot(Ranks("Gigagrug"), Character(HomeGuid, "Gigagrug"), Character(AltGuid, "Other"));

        var routes = CharacterPushRouting.Route(
            snapshot,
            [new("home", false, ["Gigagrug"]), new("other", false, ["Other"]), new("member", true, ["Gigagrug"])],
            "home");

        Assert.NotNull(routes.Single(r => r.GuildId == "home").Scope.GuildRanks);
        Assert.Null(routes.Single(r => r.GuildId == "other").Scope.GuildRanks);
        Assert.DoesNotContain(routes, r => r.GuildId == "member");
    }

    [Fact]
    public void StateOf_TreatsGuildNotAllowedAsSkipped()
    {
        var outcome = new CharacterPushOutcome(false, CharacterSyncRejectionCopy.GuildNotAllowedReason, "fp");

        Assert.Null(CharacterPushRouting.StateOf(outcome, false, null, true));
        Assert.Null(CharacterPushRouting.StateOf(outcome, true, "fp", true));
        Assert.Equal(
            ProfessionsCharacterState.Rejected,
            CharacterPushRouting.StateOf(new CharacterPushOutcome(false, CharacterSyncRejectionCopy.NotLinkedReason), false, null, true));
    }

    [Fact]
    public void StateOf_GuildNotAllowedIsPending_WhenTheCharacterWillBeResent()
    {
        var outcome = new CharacterPushOutcome(false, CharacterSyncRejectionCopy.GuildNotAllowedReason, "old");

        Assert.Equal(ProfessionsCharacterState.Pending, CharacterPushRouting.StateOf(outcome, true, "new", true));
        Assert.Equal(ProfessionsCharacterState.Pending, CharacterPushRouting.StateOf(outcome, false, null, false));
    }

    [Fact]
    public void Route_DropsGuildlessCharacters_WhenAListIsKnown()
    {
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"), Character(AltGuid, ""), Character(ThirdGuid, "  "));

        var route = Assert.Single(CharacterPushRouting.Route(snapshot, [new("a", false, ["Gigagrug"])], "a"));

        Assert.Equal([HomeGuid], Guids(route));
    }

    [Fact]
    public void Route_WithNoSelectedGuild_UsesEveryListedServer()
    {
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"), Character(AltGuid, "Other"));

        var routes = CharacterPushRouting.Route(
            snapshot,
            [new("a", false, ["Gigagrug"]), new("b", false, ["Other"]), new("c", false, null)],
            null);

        Assert.Equal(["a", "b"], routes.Select(r => r.GuildId));
    }

    [Fact]
    public void Combine_PendingWinsThenRejected_AndSkipsAreIgnored()
    {
        Assert.Null(CharacterPushRouting.Combine([null, null]));
        Assert.Equal(ProfessionsCharacterState.Synced, CharacterPushRouting.Combine([ProfessionsCharacterState.Synced, null]));
        Assert.Equal(
            ProfessionsCharacterState.Pending,
            CharacterPushRouting.Combine([ProfessionsCharacterState.Rejected, ProfessionsCharacterState.Pending]));
        Assert.Equal(
            ProfessionsCharacterState.Rejected,
            CharacterPushRouting.Combine([ProfessionsCharacterState.Synced, ProfessionsCharacterState.Rejected]));
    }
}
