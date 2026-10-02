using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class GuildAllowListTests
{
    private const string HomeGuid = "Player-1-00000001";
    private const string AltGuid = "Player-1-00000002";

    private static CharacterObservation Character(string guid, string guild) =>
        new(guid, guid, "Nightslayer", guild, 60, 1, 2, 1, null, null, false, null);

    private static SavedVariablesSnapshot Snapshot(GuildRanks? ranks = null, params CharacterObservation[] characters) => new(
        [], null, [], [], [], 0,
        characters,
        "snapshot-fingerprint",
        new Dictionary<string, CharacterProfessions>(),
        new Dictionary<string, ProfessionCatalogue>(),
        ranks,
        HasAccountData: true);

    private static GuildRanks Ranks(string guild) =>
        new("Nightslayer", guild, null, new Dictionary<int, string> { [0] = "Guild Master" });

    [Fact]
    public void IsAllowedGuild_MatchesCaseInsensitivelyOnTheTrimmedName()
    {
        Assert.True(CharacterSyncMapping.IsAllowedGuild("  gigagrug ", ["Gigagrug"]));
        Assert.True(CharacterSyncMapping.IsAllowedGuild("Gigagrug", [" GIGAGRUG"]));
        Assert.False(CharacterSyncMapping.IsAllowedGuild("Other", ["Gigagrug"]));
    }

    [Fact]
    public void IsAllowedGuild_NullListAllowsEverything_EmptyListBlocksAll()
    {
        Assert.True(CharacterSyncMapping.IsAllowedGuild("Anything", null));
        Assert.False(CharacterSyncMapping.IsAllowedGuild("Anything", []));
    }

    [Fact]
    public void Scope_KeepsOnlyAllowedCharacters()
    {
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"), Character(AltGuid, "Other"));

        var scope = CharacterSyncMapping.Scope(snapshot, false, ["gigagrug"]);

        Assert.Equal(HomeGuid, Assert.Single(scope.Characters).CharacterGuid);
    }

    [Fact]
    public void Scope_NullList_PassesEverythingAndKeepsTheSnapshotFingerprint()
    {
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"), Character(AltGuid, "Other"));

        var scope = CharacterSyncMapping.Scope(snapshot, false, null);

        Assert.Equal(2, scope.Characters.Count);
        Assert.Equal("snapshot-fingerprint", scope.Fingerprint);
    }

    [Fact]
    public void Scope_EmptyList_BlocksEverythingAndHasNoFingerprint()
    {
        var snapshot = Snapshot(Ranks("Gigagrug"), Character(HomeGuid, "Gigagrug"));

        var scope = CharacterSyncMapping.Scope(snapshot, false, []);

        Assert.Empty(scope.Characters);
        Assert.Null(scope.GuildRanks);
        Assert.Null(scope.Fingerprint);
    }

    [Fact]
    public void Scope_DropsGuildRanks_WhenItsGuildIsNotAllowed()
    {
        var snapshot = Snapshot(Ranks("Other"), Character(HomeGuid, "Gigagrug"));

        Assert.Null(CharacterSyncMapping.Scope(snapshot, false, ["Gigagrug"]).GuildRanks);
        Assert.NotNull(CharacterSyncMapping.Scope(snapshot, false, ["other"]).GuildRanks);
        Assert.NotNull(CharacterSyncMapping.Scope(snapshot, false, null).GuildRanks);
    }

    [Fact]
    public void Scope_FingerprintIgnoresAFilteredOutGuild()
    {
        var home = Character(HomeGuid, "Gigagrug");
        var withoutAlt = CharacterSyncMapping.Scope(Snapshot(null, home), false, ["Gigagrug"]);
        var withAlt = CharacterSyncMapping.Scope(Snapshot(null, home, Character(AltGuid, "Other")), false, ["Gigagrug"]);
        var altChanged = CharacterSyncMapping.Scope(Snapshot(null, home, Character(AltGuid, "Other") with { Level = 70 }), false, ["Gigagrug"]);

        Assert.Equal(withoutAlt.Fingerprint, withAlt.Fingerprint);
        Assert.Equal(withoutAlt.Fingerprint, altChanged.Fingerprint);
    }

    [Fact]
    public void Scope_ProfessionsOnly_FiltersByGuildToo()
    {
        var professions = new Dictionary<string, CharacterProfessions>
        {
            [HomeGuid] = new(null, null, null),
            [AltGuid] = new(null, null, null),
        };
        var snapshot = Snapshot(null, Character(HomeGuid, "Gigagrug"), Character(AltGuid, "Other")) with { Professions = professions };

        var scope = CharacterSyncMapping.Scope(snapshot, true, ["Gigagrug"]);

        Assert.Equal(HomeGuid, Assert.Single(scope.Characters).CharacterGuid);
        Assert.Null(scope.GuildRanks);
    }

    [Fact]
    public void RejectionCopy_DescribesGuildNotAllowed()
    {
        Assert.Equal(
            "Not in a WoW guild this server syncs",
            CharacterSyncRejectionCopy.Describe(CharacterSyncRejectionCopy.GuildNotAllowedReason));
    }

    [Fact]
    public void AdminGuild_ParsesSyncGuildNames_WhenPresent()
    {
        var me = JsonSerializer.Deserialize(
            """{"user":{"id":"1","name":"H","username":null,"avatar_url":null,"role":null},"guilds":[{"id":"a","name":"A","icon_url":null,"member_count":1,"nick":null,"sync_guild_names":["Gigagrug","Other"]}]}""",
            CompanionJsonContext.Default.AdminMe)!;

        Assert.Equal(["Gigagrug", "Other"], me.Guilds[0].SyncGuildNames);
    }

    [Fact]
    public void AdminGuild_SyncGuildNamesIsNull_WhenAbsent()
    {
        var me = JsonSerializer.Deserialize(
            """{"user":{"id":"1","name":"H","username":null,"avatar_url":null,"role":null},"guilds":[{"id":"a","name":"A","icon_url":null,"member_count":1,"nick":null}]}""",
            CompanionJsonContext.Default.AdminMe)!;

        Assert.Null(me.Guilds[0].SyncGuildNames);
    }
}
