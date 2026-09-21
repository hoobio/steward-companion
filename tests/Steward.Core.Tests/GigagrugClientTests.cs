using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class GigagrugClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (GigagrugClient Client, StubHandler Handler) ClientFor(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new GigagrugClient(new HttpClient(handler), "https://api.example.com/guild"), handler);
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
        Assert.Equal(["Officer", "Raider"], statuses);
        var origin = Assert.Single(origins);
        Assert.Equal("EU", origin.Name);
        Assert.Equal("#1d7fd6", origin.Color);
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
}
