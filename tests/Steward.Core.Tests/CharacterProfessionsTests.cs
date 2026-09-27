using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class CharacterProfessionsTests
{
    [Fact]
    public void CharacterSyncEntry_OmitsProfessions_WhenNoneObserved()
    {
        var entry = new CharacterSyncEntry(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null);

        var json = JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry);

        Assert.DoesNotContain("professions", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterSyncEntry_SerialisesProfessions_OmittingAbsentFields()
    {
        var professions = new CharacterProfessions(
            1758260000,
            [new ProfessionSkill("Alchemy", 285, 300, false)],
            new Dictionary<string, ProfessionRecipes>
            {
                ["Alchemy"] = new ProfessionRecipes(
                    1758260000,
                    [new ProfessionRecipe(
                        "Major Healing Potion",
                        11460,
                        "Potions",
                        "optimal",
                        13446,
                        string.Empty,
                        [new ProfessionReagent("Golden Sansam", 13464, 2)])]),
            });

        var entry = new CharacterSyncEntry(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1,
            1758250000, "123456789012345678", true, 1758260000, professions);

        var json = JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Player-4395-0A1B2C3D", root.GetProperty("guid").GetString());
        var professionsElement = root.GetProperty("professions");
        Assert.Equal(1758260000, professionsElement.GetProperty("observedAt").GetInt64());

        var skill = Assert.Single(professionsElement.GetProperty("skills").EnumerateArray());
        Assert.Equal("Alchemy", skill.GetProperty("name").GetString());
        Assert.Equal(285, skill.GetProperty("rank").GetInt32());
        Assert.False(skill.TryGetProperty("secondary", out var secondary) && secondary.ValueKind == JsonValueKind.Null);

        var recipe = Assert.Single(professionsElement.GetProperty("recipes").GetProperty("Alchemy").GetProperty("list").EnumerateArray());
        Assert.Equal("Major Healing Potion", recipe.GetProperty("name").GetString());
        Assert.Equal(11460, recipe.GetProperty("recipeId").GetInt32());
        Assert.False(recipe.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Null);
        Assert.Equal(string.Empty, recipe.GetProperty("tools").GetString());

        var reagent = Assert.Single(recipe.GetProperty("reagents").EnumerateArray());
        Assert.Equal("Golden Sansam", reagent.GetProperty("name").GetString());
        Assert.Equal(13464, reagent.GetProperty("itemId").GetInt32());
        Assert.Equal(2, reagent.GetProperty("count").GetInt32());
    }

    [Fact]
    public void CharacterSyncEntry_OmitsNullOptionalFields_WithinProfessions()
    {
        var professions = new CharacterProfessions(null, [new ProfessionSkill("Cooking", null, null, null)], null);
        var entry = new CharacterSyncEntry(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null, professions);

        var json = JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry);
        using var doc = JsonDocument.Parse(json);
        var professionsElement = doc.RootElement.GetProperty("professions");

        Assert.False(professionsElement.TryGetProperty("observedAt", out _));
        Assert.False(professionsElement.TryGetProperty("recipes", out _));
        var skill = Assert.Single(professionsElement.GetProperty("skills").EnumerateArray());
        Assert.False(skill.TryGetProperty("rank", out _));
        Assert.False(skill.TryGetProperty("maxRank", out _));
        Assert.False(skill.TryGetProperty("secondary", out _));
    }

    [Fact]
    public void CharacterSyncEntry_SerialisesFp_WhenPresent()
    {
        var professions = new CharacterProfessions(null, null, null, "a1b2c3d4");
        var entry = new CharacterSyncEntry(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null, professions);

        var json = JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("a1b2c3d4", doc.RootElement.GetProperty("professions").GetProperty("fp").GetString());
    }

    [Fact]
    public void CharacterSyncEntry_OmitsFp_WhenAbsent()
    {
        var professions = new CharacterProfessions(null, null, null);
        var entry = new CharacterSyncEntry(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null, professions);

        var json = JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("professions").TryGetProperty("fp", out _));
    }

    [Fact]
    public void Fingerprint_ChangesWhenFpChanges()
    {
        var observation = new CharacterObservation(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null);

        var withOneFp = CharacterSyncMapping.Fingerprint(
            [observation], new Dictionary<string, CharacterProfessions> { ["Player-4395-0A1B2C3D"] = new CharacterProfessions(null, null, null, "a1b2c3d4") });
        var withAnotherFp = CharacterSyncMapping.Fingerprint(
            [observation], new Dictionary<string, CharacterProfessions> { ["Player-4395-0A1B2C3D"] = new CharacterProfessions(null, null, null, "deadbeef") });

        Assert.NotEqual(withOneFp, withAnotherFp);
    }

    [Fact]
    public void ToEntry_AttachesProfessions_ByMatchingGuid()
    {
        var observation = new CharacterObservation(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null);
        var professions = new Dictionary<string, CharacterProfessions>
        {
            ["Player-4395-0A1B2C3D"] = new CharacterProfessions(1758260000, null, null),
            ["Player-4395-DEADBEEF"] = new CharacterProfessions(1758260000, null, null),
        };

        var entry = CharacterSyncMapping.ToEntry(observation, professions);

        Assert.NotNull(entry.Professions);
        Assert.Equal(1758260000, entry.Professions!.ObservedAt);
    }

    [Fact]
    public void ToEntry_LeavesProfessionsNull_WhenNoObservationMatchesTheGuid()
    {
        var observation = new CharacterObservation(
            "Player-4395-11111111", "Grug", "Nightslayer", "Gigagrug", 58, 7, 3, 2, null, null, false, null);
        var professions = new Dictionary<string, CharacterProfessions>
        {
            ["Player-4395-0A1B2C3D"] = new CharacterProfessions(1758260000, null, null),
        };

        var entry = CharacterSyncMapping.ToEntry(observation, professions);

        Assert.Null(entry.Professions);
    }

    [Fact]
    public void ToSync_MapsGuildRanks_WithStringKeys()
    {
        var ranks = new GuildRanks(
            "Nightslayer", "Gigagrug", DateTimeOffset.FromUnixTimeSeconds(1758260000),
            new Dictionary<int, string> { [1] = "Guild Master", [2] = "Officer" });

        var sync = CharacterSyncMapping.ToSync(ranks);

        Assert.Equal("Nightslayer", sync.Realm);
        Assert.Equal("Gigagrug", sync.Guild);
        Assert.Equal(1758260000, sync.ObservedAt);
        Assert.Equal("Guild Master", sync.Ranks["1"]);
        Assert.Equal("Officer", sync.Ranks["2"]);
    }

    [Fact]
    public void CharacterSyncRequest_OmitsGuildRanks_WhenAbsent()
    {
        var request = new CharacterSyncRequest("batch-1", "1.0.0", []);

        var json = JsonSerializer.Serialize(request, CompanionJsonContext.Default.CharacterSyncRequest);

        Assert.DoesNotContain("guildRanks", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterToProfessionsOnly_KeepsOnlyCharactersWithAProfessionsEntry()
    {
        var linked = new CharacterObservation(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null);
        var unlinked = new CharacterObservation(
            "Player-4395-11111111", "Grug", "Nightslayer", "Gigagrug", 58, 7, 3, 2, null, null, false, null);
        var snapshot = new SavedVariablesSnapshot(
            [], null, [], [], [], 0,
            [linked, unlinked],
            "irrelevant",
            new Dictionary<string, CharacterProfessions> { ["Player-4395-0A1B2C3D"] = new CharacterProfessions(null, null, null) },
            new Dictionary<string, ProfessionCatalogue>());

        var filtered = CharacterSyncMapping.FilterToProfessionsOnly(snapshot);

        var character = Assert.Single(filtered);
        Assert.Equal("Player-4395-0A1B2C3D", character.CharacterGuid);
    }

    [Fact]
    public void Fingerprint_IgnoresCatalogueAndGuildRanks_WhenOmitted()
    {
        var observation = new CharacterObservation(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null);
        var professions = new Dictionary<string, CharacterProfessions>
        {
            ["Player-4395-0A1B2C3D"] = new CharacterProfessions(1758260000, null, null),
        };

        var withoutExtras = CharacterSyncMapping.Fingerprint([observation], professions);
        var withCatalogueAndRanks = CharacterSyncMapping.Fingerprint(
            [observation],
            professions,
            new Dictionary<string, ProfessionCatalogue> { ["Alchemy"] = new ProfessionCatalogue(null, []) },
            new GuildRanks("Nightslayer", "Gigagrug", null, new Dictionary<int, string> { [1] = "Officer" }));

        Assert.NotEqual(withoutExtras, withCatalogueAndRanks);
    }

    [Fact]
    public void Fingerprint_ChangesWhenTheFilteredCharacterSetChanges()
    {
        var one = new CharacterObservation(
            "Player-4395-0A1B2C3D", "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null);
        var two = new CharacterObservation(
            "Player-4395-11111111", "Grug", "Nightslayer", "Gigagrug", 58, 7, 3, 2, null, null, false, null);
        var professions = new Dictionary<string, CharacterProfessions>
        {
            ["Player-4395-0A1B2C3D"] = new CharacterProfessions(null, null, null),
            ["Player-4395-11111111"] = new CharacterProfessions(null, null, null),
        };

        var withOne = CharacterSyncMapping.Fingerprint([one], professions);
        var withBoth = CharacterSyncMapping.Fingerprint([one, two], professions);

        Assert.NotEqual(withOne, withBoth);
    }

    [Fact]
    public void CharacterSyncRequest_SerialisesGuildRanks_WithCamelCaseStringKeys()
    {
        var ranks = new GuildRanks(
            "Nightslayer", "Gigagrug", DateTimeOffset.FromUnixTimeSeconds(1758260000),
            new Dictionary<int, string> { [1] = "Guild Master" });
        var request = new CharacterSyncRequest("batch-1", "1.0.0", [], null, CharacterSyncMapping.ToSync(ranks));

        var json = JsonSerializer.Serialize(request, CompanionJsonContext.Default.CharacterSyncRequest);
        using var doc = JsonDocument.Parse(json);
        var guildRanks = doc.RootElement.GetProperty("guildRanks");

        Assert.Equal("Nightslayer", guildRanks.GetProperty("realm").GetString());
        Assert.Equal("Gigagrug", guildRanks.GetProperty("guild").GetString());
        Assert.Equal(1758260000, guildRanks.GetProperty("observedAt").GetInt64());
        Assert.Equal("Guild Master", guildRanks.GetProperty("ranks").GetProperty("1").GetString());
    }

    [Fact]
    public void MemberCatalogueMapping_ToCatalogue_MapsEachRecipeAndReagent()
    {
        var directoryCatalogue = new Dictionary<string, IReadOnlyList<DirectoryRecipe>>
        {
            ["Alchemy"] =
            [
                new DirectoryRecipe(11460, "Major Healing Potion", "optimal", "Potions", 13446, string.Empty,
                    [new DirectoryReagent(13464, "Golden Sansam", 2)]),
            ],
        };

        var catalogue = MemberCatalogueMapping.ToCatalogue(directoryCatalogue);

        var recipe = Assert.Single(catalogue["Alchemy"]);
        Assert.Equal("Major Healing Potion", recipe.Name);
        Assert.Equal(11460, recipe.RecipeId);
        Assert.Equal("Potions", recipe.Header);
        Assert.Equal(13446, recipe.ItemId);
        var reagent = Assert.Single(recipe.Reagents!);
        Assert.Equal("Golden Sansam", reagent.Name);
        Assert.Equal(13464, reagent.ItemId);
        Assert.Equal(2, reagent.Count);
    }
}
