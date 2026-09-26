using System.Net;
using System.Text;
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

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task GetRecipeCatalogueAsync_Success_Deserialises()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"catalogue":{"Alchemy":[{"recipeId":11460,"name":"Major Healing Potion","header":"Potions","itemId":13446,"tools":"","reagents":[{"itemId":13464,"name":"Golden Sansam","count":2}]}]}}
            """);
        var client = new GigagrugClient(new HttpClient(handler), "https://api.example.com/guild");

        var catalogue = await client.GetRecipeCatalogueAsync("1", CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/admin/1/recipes/catalogue", handler.RequestUrl);
        var recipe = Assert.Single(catalogue["Alchemy"]);
        Assert.Equal("Major Healing Potion", recipe.Name);
        Assert.Equal(11460, recipe.RecipeId);
    }

    [Fact]
    public async Task GetRecipeCatalogueAsync_Unauthorized_ThrowsSessionExpired()
    {
        var client = new GigagrugClient(new HttpClient(new StubHandler(HttpStatusCode.Unauthorized, "{}")), "https://api.example.com/guild");

        await Assert.ThrowsAsync<SessionExpiredException>(() => client.GetRecipeCatalogueAsync("1", CancellationToken.None));
    }

    [Fact]
    public async Task GetRecipeCatalogueAsync_NotFound_ThrowsHttpRequestException()
    {
        var client = new GigagrugClient(new HttpClient(new StubHandler(HttpStatusCode.NotFound, "{}")), "https://api.example.com/guild");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetRecipeCatalogueAsync("1", CancellationToken.None));
        Assert.Contains("404", exception.Message, StringComparison.Ordinal);
    }
}
