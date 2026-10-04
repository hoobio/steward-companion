using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class StewardClientTests
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

    private static (StewardClient Client, StubHandler Handler) ClientFor(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new StewardClient(new HttpClient(handler), "https://api.example.com/guild", "Steward/1.0.0 (dev)"), handler);
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
    public async Task PostCharacterSyncAsync_TooManyRequests_ThrowsStewardThrottledExceptionRatherThanUnreachable()
    {
        var (client, _) = ClientFor(HttpStatusCode.TooManyRequests, "{}");
        var request = new CharacterSyncRequest("batch-1", "1.0.0", []);

        await Assert.ThrowsAsync<StewardThrottledException>(
            () => client.PostCharacterSyncAsync("1", request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostCharacterSyncAsync_OtherClientError_ThrowsStewardRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.BadRequest, "bad batch");
        var request = new CharacterSyncRequest("batch-1", "1.0.0", []);

        var exception = await Assert.ThrowsAsync<StewardRequestException>(
            () => client.PostCharacterSyncAsync("1", request, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task PostCharacterSyncAsync_PostsToTheMemberScopedRoute()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"accepted":1,"rejected":[]}""");
        var request = new CharacterSyncRequest("batch-1", "1.0.0", []);

        await client.PostCharacterSyncAsync("1", request, TestContext.Current.CancellationToken);

        Assert.Equal("https://api.example.com/guild/api/sync/1/characters", handler.RequestUrl);
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

    [Fact]
    public async Task GetMeAsync_WithoutAddons_LeavesTheCatalogueUnknown()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, """{"user":{"id":"1","name":"Hoobi"},"guilds":[]}""");

        var me = await client.GetMeAsync(CancellationToken.None);

        Assert.Null(me.Addons);
    }

    [Fact]
    public async Task GetMeAsync_WithAddons_MapsEachEntryToAManagedAddon()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK,
            """
            {"user":{"id":"1","name":"Hoobi"},"guilds":[],"addons":[
            {"id":"steward","name":"Steward","folder_name":"Steward","manifest_base_url":"https://addon.hoobi.io/steward/","source":"Steward","auto_install":true,"features":["steward_addon"]},
            {"id":"hoobiscripts-actionbars","name":"Hoobi Scripts: ActionBars","folder_name":"HoobiScripts_ActionBars","manifest_base_url":"https://addon.hoobi.io/hoobiscripts-actionbars/","source":"Steward","auto_install":false,"features":["hoobiscripts.actionbars"]}]}
            """);

        var me = await client.GetMeAsync(CancellationToken.None);

        var addons = me.Addons!.Select(addon => addon.ToManagedAddon()).ToList();
        Assert.Equal(["steward", "hoobiscripts-actionbars"], addons.Select(addon => addon.Id));
        Assert.Equal("HoobiScripts_ActionBars", addons[1].FolderName);
        Assert.Equal("Hoobi Scripts: ActionBars", addons[1].DisplayName);
        Assert.Equal("https://addon.hoobi.io/hoobiscripts-actionbars/", addons[1].ManifestBaseUrl);
        Assert.True(addons[0].AutoInstall);
        Assert.False(addons[1].AutoInstall);
        Assert.Equal("Steward", addons[0].Source);
        Assert.Equal(["steward_addon"], addons[0].Features);
    }

    [Fact]
    public async Task GetMeAsync_SkipsAnIncompleteAddonAndDefaultsNullFields()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK,
            """
            {"user":{"id":"1","name":"Hoobi"},"guilds":[],"addons":[
            {"name":"No id","folder_name":"NoId","manifest_base_url":"https://addon.hoobi.io/noid/"},
            {"id":"nofolder","manifest_base_url":"https://addon.hoobi.io/nofolder/"},
            {"id":"nomanifest","folder_name":"NoManifest","manifest_base_url":null},
            null,
            {"id":"steward","folder_name":"Steward","manifest_base_url":"https://addon.hoobi.io/steward/","source":null,"auto_install":null}]}
            """);

        var me = await client.GetMeAsync(CancellationToken.None);

        var addon = Assert.Single(me.Addons!).ToManagedAddon();
        Assert.Equal("steward", addon.Id);
        Assert.False(addon.AutoInstall);
        Assert.Equal("Steward", addon.Source);
    }

    [Fact]
    public async Task GetMeAsync_WithAnEmptyAddonList_KeepsItEmptyRatherThanUnknown()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, """{"user":{"id":"1","name":"Hoobi"},"guilds":[],"addons":[]}""");

        var me = await client.GetMeAsync(CancellationToken.None);

        Assert.NotNull(me.Addons);
        Assert.Empty(me.Addons);
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
    public async Task GetMemberRosterAsync_Forbidden_ThrowsStewardRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.Forbidden, "{}");

        var exception = await Assert.ThrowsAsync<StewardRequestException>(
            () => client.GetMemberRosterAsync("1", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    private const string ProfessionsBody =
        """
        {"professions":[{"guid":"Player-4395-0A1B2C3D","name":"Hoobi","class_id":1,
        "skills":[{"name":"Alchemy","rank":285,"max_rank":300,"secondary":false}],
        "recipes":{"Alchemy":[11460,11461]}}],
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
        Assert.Equal([11460, 11461], profession.Recipes["Alchemy"]);
        Assert.Null(Assert.Single(response.Catalogue["Alchemy"]).Difficulty);
    }

    [Fact]
    public async Task GetMemberProfessionsAsync_Forbidden_ThrowsStewardRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.Forbidden, "{}");

        var exception = await Assert.ThrowsAsync<StewardRequestException>(
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
    public async Task GetMemberCatalogueAsync_Forbidden_ThrowsStewardRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.Forbidden, "{}");

        var exception = await Assert.ThrowsAsync<StewardRequestException>(
            () => client.GetMemberCatalogueAsync("1", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task StreamAccessEventsAsync_Success_YieldsTheTypeFromEachDataPayload()
    {
        var (client, handler) = ClientFor(
            HttpStatusCode.OK,
            "data: {\"type\": \"connected\"}\n\ndata: {\"type\": \"accessChanged\"}\n\ndata: {\"type\": \"adminSeats\", \"guild\": \"1\"}\n\n");

        var events = new List<string>();
        await foreach (var eventType in client.StreamAccessEventsAsync(TestContext.Current.CancellationToken))
        {
            events.Add(eventType);
        }

        Assert.Equal("https://api.example.com/guild/api/events", handler.RequestUrl);
        Assert.Equal(["connected", "accessChanged", "adminSeats"], events);
    }

    [Fact]
    public async Task StreamAccessEventsAsync_Success_YieldsBannersChanged()
    {
        var (client, _) = ClientFor(
            HttpStatusCode.OK,
            "data: {\"type\": \"bannersChanged\"}\n\n");

        var events = new List<string>();
        await foreach (var eventType in client.StreamAccessEventsAsync(TestContext.Current.CancellationToken))
        {
            events.Add(eventType);
        }

        Assert.Equal(["bannersChanged"], events);
    }

    [Fact]
    public async Task StreamAccessEventsAsync_IgnoresAFrameWithNoTypeOrUnparsableData()
    {
        var (client, _) = ClientFor(
            HttpStatusCode.OK,
            "data: {}\n\ndata: not json\n\ndata: {\"type\": \"accessChanged\"}\n\n");

        var events = new List<string>();
        await foreach (var eventType in client.StreamAccessEventsAsync(TestContext.Current.CancellationToken))
        {
            events.Add(eventType);
        }

        Assert.Equal(["accessChanged"], events);
    }

    [Fact]
    public async Task StreamAccessEventsAsync_Unauthorized_ThrowsSessionExpired()
    {
        var (client, _) = ClientFor(HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<SessionExpiredException>(async () =>
        {
            await foreach (var _ in client.StreamAccessEventsAsync(TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task StreamAccessEventsAsync_NotFound_ThrowsStewardRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.NotFound, "{}");

        var exception = await Assert.ThrowsAsync<StewardRequestException>(async () =>
        {
            await foreach (var _ in client.StreamAccessEventsAsync(TestContext.Current.CancellationToken))
            {
            }
        });
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task SetSelectedGuildAsync_Success_PutsTheGuildIdToTheMeGuildRoute()
    {
        var (client, handler) = ClientFor(HttpStatusCode.NoContent, "");

        await client.SetSelectedGuildAsync("1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal("https://api.example.com/guild/api/me/guild", handler.RequestUrl);
        var body = JsonSerializer.Deserialize(
            handler.RequestContent!, CompanionJsonContext.Default.SelectedGuildRequest);
        Assert.Equal("1", body?.GuildId);
    }

    [Fact]
    public async Task SetSelectedGuildAsync_Unauthorized_ThrowsSessionExpired()
    {
        var (client, _) = ClientFor(HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<SessionExpiredException>(
            () => client.SetSelectedGuildAsync("1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetSelectedGuildAsync_BadRequest_ThrowsStewardRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.BadRequest, "{}");

        var exception = await Assert.ThrowsAsync<StewardRequestException>(
            () => client.SetSelectedGuildAsync("1", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task SetSelectedGuildAsync_NotFound_ThrowsStewardRequestException()
    {
        var (client, _) = ClientFor(HttpStatusCode.NotFound, "{}");

        var exception = await Assert.ThrowsAsync<StewardRequestException>(
            () => client.SetSelectedGuildAsync("1", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
}
