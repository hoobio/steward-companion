using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class RecipeCatalogueTests
{
    [Fact]
    public void CharacterSyncRequest_OmitsCatalogue_WhenNone()
    {
        var request = new CharacterSyncRequest("batch-1", "0.12.0", []);

        var json = JsonSerializer.Serialize(request, CompanionJsonContext.Default.CharacterSyncRequest);

        Assert.DoesNotContain("catalogue", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterSyncRequest_SerialisesCatalogue_OmittingAbsentFields()
    {
        var catalogue = new Dictionary<string, ProfessionCatalogue>
        {
            ["Alchemy"] = new ProfessionCatalogue(
                1758260000,
                [new CatalogueRecipe("Major Healing Potion", 11460, "Potions", 13446, string.Empty,
                    [new ProfessionReagent("Golden Sansam", 13464, 2)])]),
        };
        var request = new CharacterSyncRequest("batch-1", "0.12.0", [], catalogue);

        var json = JsonSerializer.Serialize(request, CompanionJsonContext.Default.CharacterSyncRequest);
        using var doc = JsonDocument.Parse(json);
        var alchemy = doc.RootElement.GetProperty("catalogue").GetProperty("Alchemy");

        Assert.Equal(1758260000, alchemy.GetProperty("scannedAt").GetInt64());
        var recipe = Assert.Single(alchemy.GetProperty("list").EnumerateArray());
        Assert.Equal("Major Healing Potion", recipe.GetProperty("name").GetString());
        Assert.Equal(11460, recipe.GetProperty("recipeId").GetInt32());
        Assert.False(recipe.TryGetProperty("difficulty", out _));
        Assert.Equal(2, recipe.GetProperty("reagents").EnumerateArray().Single().GetProperty("count").GetInt32());
    }
}
