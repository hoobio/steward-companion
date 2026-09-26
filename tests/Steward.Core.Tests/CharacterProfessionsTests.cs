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
}
