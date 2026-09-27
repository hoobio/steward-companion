using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class GigagrugClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestUrl { get; private set; }

        public HttpRequestMessage? Request { get; private set; }

        public byte[]? RequestContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUrl = request.RequestUri?.ToString();
            Request = request;
            RequestContent = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (GigagrugClient Client, StubHandler Handler) ClientFor(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new GigagrugClient(new HttpClient(handler), "https://api.example.com/guild", "Steward/1.0.0 (dev)"), handler);
    }

    private const string RosterBody =
        """
        {"members":[{"user_id":"1","name":"Hoobi","display_name":null,"discord_tag":"hoobi#0001",
        "status":"Raider","origin":["EU"],"flags":[],"rating":null,"notes":"Y\nGood raider",
        "notes_warning":false,"signups":5,"last_signup_at":123,
        "primary":{"class":"WARRIOR","spec":"Fury","role":"Melee"},"secondary":null}],
        "statuses":[{"name":"Officer","color":"#ff0000","role_id":"9","position":0},
        {"name":"Raider","color":"#00ff00","role_id":null,"position":1}],
        "origins":[{"name":"EU","color":"#1d7fd6","position":0}]}
        """;

    private const string MembersBody =
        """{"members":[{"id":"1","name":"Grug","nick":"G","avatar_url":"https://example.com/a.png"}]}""";

    [Fact]
    public async Task GetGuildRosterAsync_Success_TrimsTheNoteAndDeserialises()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, RosterBody);

        var (members, statuses, origins) = await client.GetGuildRosterAsync("1", CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/admin/1/roster", handler.RequestUrl);
        var member = Assert.Single(members);
        Assert.Equal("1", member.UserId);
        Assert.Equal("Good raider", member.Notes);
        Assert.Equal("WARRIOR", member.Primary?.Class);
        Assert.Null(member.Secondary);
        Assert.Null(member.Main);
        Assert.Equal(["Officer", "Raider"], statuses);
        var origin = Assert.Single(origins);
        Assert.Equal("EU", origin.Name);
        Assert.Equal("#1d7fd6", origin.Color);
    }

    [Fact]
    public async Task GetGuildRosterAsync_Success_DeserialisesTheMain_WhenPresent()
    {
        const string body =
            """
            {"members":[{"user_id":"1","name":"Grug","display_name":null,"discord_tag":null,
            "status":null,"origin":[],"flags":[],"rating":null,"notes":null,
            "notes_warning":false,"signups":0,"last_signup_at":0,
            "primary":null,"secondary":null,
            "main":{"guid":"Player-4619-00B33CCD","name":"Hoobi Furry","level":60,"class_id":1}}]}
            """;
        var (client, _) = ClientFor(HttpStatusCode.OK, body);

        var (members, _, _) = await client.GetGuildRosterAsync("1", CancellationToken.None);

        var main = Assert.Single(members).Main;
        Assert.Equal("Player-4619-00B33CCD", main?.CharacterGuid);
        Assert.Equal("Hoobi Furry", main?.Name);
        Assert.Equal(60, main?.Level);
        Assert.Equal(1, main?.ClassId);
    }

    [Fact]
    public async Task GetGuildRosterAsync_Success_GivesAnEmptyStatusesAndOriginsList_WhenTheFieldsAreAbsent()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, """{"members":[]}""");

        var (_, statuses, origins) = await client.GetGuildRosterAsync("1", CancellationToken.None);

        Assert.Empty(statuses);
        Assert.Empty(origins);
    }

    [Fact]
    public async Task GetGuildRosterAsync_Unauthorized_ThrowsSessionExpired()
    {
        var (client, _) = ClientFor(HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<SessionExpiredException>(
            () => client.GetGuildRosterAsync("1", CancellationToken.None));
    }

    [Fact]
    public async Task GetGuildRosterAsync_ServerError_ThrowsHttpRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.InternalServerError, "{}");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetGuildRosterAsync("1", CancellationToken.None));
        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDiscordMembersAsync_Success_Deserialises()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, MembersBody);

        var members = await client.GetDiscordMembersAsync("1", CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/admin/1/members", handler.RequestUrl);
        var member = Assert.Single(members);
        Assert.Equal("1", member.Id);
        Assert.Equal("Grug", member.Name);
        Assert.Equal("G", member.Nick);
    }

    [Fact]
    public async Task GetDiscordMembersAsync_Unauthorized_ThrowsSessionExpired()
    {
        var (client, _) = ClientFor(HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<SessionExpiredException>(
            () => client.GetDiscordMembersAsync("1", CancellationToken.None));
    }

    [Fact]
    public async Task GetDiscordMembersAsync_ServerError_ThrowsHttpRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.InternalServerError, "{}");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetDiscordMembersAsync("1", CancellationToken.None));
        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostCharacterSyncAsync_SendsAGzipCompressedBody()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"accepted":1,"rejected":[]}""");
        var request = new CharacterSyncRequest(
            "batch-1",
            "1.0.0",
            [new CharacterSyncEntry("guid-1", "Hoobi", "Nightslayer", "Grug's Guild", 60, 1, 1, 0, null, null, false, null)]);

        await client.PostCharacterSyncAsync("1", request, TestContext.Current.CancellationToken);

        Assert.Equal("gzip", Assert.Single(handler.Request!.Content!.Headers.ContentEncoding));
        Assert.Equal("application/json", handler.Request.Content.Headers.ContentType?.MediaType);

        await using var gzip = new GZipStream(new MemoryStream(handler.RequestContent!), CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        await gzip.CopyToAsync(decompressed, TestContext.Current.CancellationToken);
        var deserialised = JsonSerializer.Deserialize(
            decompressed.ToArray(), CompanionJsonContext.Default.CharacterSyncRequest);

        Assert.Equal(request.BatchId, deserialised?.BatchId);
        Assert.Equal(request.AppVersion, deserialised?.AppVersion);
        Assert.Equal(request.Characters, deserialised?.Characters);
    }

    [Fact]
    public async Task PostCharacterSyncAsync_RequestTimeout_ThrowsHttpRequestExceptionRatherThanRecordingAPermanentFailure()
    {
        var (client, _) = ClientFor(HttpStatusCode.RequestTimeout, "{}");
        var request = new CharacterSyncRequest("batch-1", "1.0.0", []);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PostCharacterSyncAsync("1", request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostCharacterSyncAsync_TooManyRequests_ThrowsGigagrugThrottledExceptionRatherThanUnreachable()
    {
        var (client, _) = ClientFor(HttpStatusCode.TooManyRequests, "{}");
        var request = new CharacterSyncRequest("batch-1", "1.0.0", []);

        await Assert.ThrowsAsync<GigagrugThrottledException>(
            () => client.PostCharacterSyncAsync("1", request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostCharacterSyncAsync_OtherClientError_ThrowsGigagrugRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.BadRequest, "bad batch");
        var request = new CharacterSyncRequest("batch-1", "1.0.0", []);

        var exception = await Assert.ThrowsAsync<GigagrugRequestException>(
            () => client.PostCharacterSyncAsync("1", request, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task PostCharacterSyncAsync_PostsToTheMemberScopedRoute()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"accepted":1,"rejected":[]}""");
        var request = new CharacterSyncRequest("batch-1", "1.0.0", []);

        await client.PostCharacterSyncAsync("1", request, TestContext.Current.CancellationToken);

        Assert.Equal("https://api.example.com/guild/api/guild/1/characters/sync", handler.RequestUrl);
    }

    [Fact]
    public async Task Constructor_SetsTheUserAgentOnEveryRequest()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"user":{"id":"1","name":"Hoobi"},"guilds":[]}""");

        await client.GetMeAsync(CancellationToken.None);

        Assert.Equal("Steward/1.0.0 (dev)", handler.Request!.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task GetMeAsync_CallsTheTopLevelRoute()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"user":{"id":"1","name":"Hoobi"},"guilds":[]}""");

        await client.GetMeAsync(CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/me", handler.RequestUrl);
    }

    private const string MemberRosterBody =
        """
        {"people":[{"id":"1","name":"Hoobi","main_guid":"Player-4395-0A1B2C3D"}],
        "characters":[{"guid":"Player-4395-0A1B2C3D","name":"Hoobi","level":60,"class_id":1,"linked_user_id":"1"}]}
        """;

    [Fact]
    public async Task GetMemberRosterAsync_Success_CallsTheGuildRouteAndDeserialises()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, MemberRosterBody);

        var roster = await client.GetMemberRosterAsync("1", CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/guild/1/roster", handler.RequestUrl);
        var person = Assert.Single(roster.People);
        Assert.Equal("Player-4395-0A1B2C3D", person.MainGuid);
        var character = Assert.Single(roster.Characters);
        Assert.Equal(60, character.Level);
    }

    [Fact]
    public async Task GetMemberRosterAsync_Forbidden_ThrowsGigagrugRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.Forbidden, "{}");

        var exception = await Assert.ThrowsAsync<GigagrugRequestException>(
            () => client.GetMemberRosterAsync("1", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    private const string ProfessionsBody =
        """
        {"professions":[{"guid":"Player-4395-0A1B2C3D","name":"Hoobi","class_id":1,
        "skills":[{"name":"Alchemy","rank":285,"max_rank":300,"secondary":false}],
        "recipes":{"Alchemy":[{"recipe_id":11460,"name":"Major Healing Potion","difficulty":"optimal","header":"Potions",
        "item_id":13446,"tools":"","reagents":[{"item_id":13464,"name":"Golden Sansam","count":2}]}]}}],
        "catalogue":{"Alchemy":[{"recipe_id":11460,"name":"Major Healing Potion","header":"Potions","item_id":13446,
        "tools":"","reagents":[]}]}}
        """;

    [Fact]
    public async Task GetMemberProfessionsAsync_Success_CallsTheGuildRouteAndDeserialises()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, ProfessionsBody);

        var response = await client.GetMemberProfessionsAsync("1", CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/guild/1/professions", handler.RequestUrl);
        var profession = Assert.Single(response.Professions);
        Assert.Equal("Hoobi", profession.Name);
        Assert.Equal(1, profession.ClassId);
        var recipe = Assert.Single(profession.Recipes["Alchemy"]);
        Assert.Equal("optimal", recipe.Difficulty);
        Assert.Null(Assert.Single(response.Catalogue["Alchemy"]).Difficulty);
    }

    [Fact]
    public async Task GetMemberProfessionsAsync_Forbidden_ThrowsGigagrugRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.Forbidden, "{}");

        var exception = await Assert.ThrowsAsync<GigagrugRequestException>(
            () => client.GetMemberProfessionsAsync("1", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task GetMemberCatalogueAsync_Success_CallsTheGuildRouteAndDeserialises()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"catalogue":{"Alchemy":[]}}""");

        var catalogue = await client.GetMemberCatalogueAsync("1", CancellationToken.None);

        Assert.Equal("https://api.example.com/guild/api/guild/1/recipes/catalogue", handler.RequestUrl);
        Assert.True(catalogue.ContainsKey("Alchemy"));
    }

    [Fact]
    public async Task GetMemberCatalogueAsync_Forbidden_ThrowsGigagrugRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.Forbidden, "{}");

        var exception = await Assert.ThrowsAsync<GigagrugRequestException>(
            () => client.GetMemberCatalogueAsync("1", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }
}
