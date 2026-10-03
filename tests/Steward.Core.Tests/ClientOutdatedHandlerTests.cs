using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class ClientOutdatedHandlerTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static HttpClient ClientFor(HttpStatusCode status, string body, Action<string>? onOutdated = null) =>
        new(new ClientOutdatedHandler(onOutdated) { InnerHandler = new StubHandler(status, body) });

    [Fact]
    public async Task Gone_WithClientOutdated_ThrowsWithTheServerMessage()
    {
        string? notified = null;
        using var client = ClientFor(HttpStatusCode.Gone, """{"error":"client_outdated","message":"Update Steward to 0.13.0 or newer."}""", m => notified = m);

        var ex = await Assert.ThrowsAsync<ClientOutdatedException>(() => client.GetAsync("https://api.example.com/guild/api/admin/me", TestContext.Current.CancellationToken));

        Assert.Equal("Update Steward to 0.13.0 or newer.", ex.Message);
        Assert.Equal(HttpStatusCode.Gone, ex.StatusCode);
        Assert.Equal(ex.Message, notified);
    }

    [Fact]
    public async Task Gone_WithClientOutdatedAndNoMessage_FallsBackToTheFixedSentence()
    {
        using var client = ClientFor(HttpStatusCode.Gone, """{"error":"client_outdated","message":null}""");

        var ex = await Assert.ThrowsAsync<ClientOutdatedException>(() => client.GetAsync("https://api.example.com/guild/api/me", TestContext.Current.CancellationToken));

        Assert.Equal(ClientOutdatedException.FallbackMessage, ex.Message);
    }

    [Theory]
    [InlineData("""{"error":"gone"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task Gone_WithAnyOtherBody_PassesThrough(string body)
    {
        using var client = ClientFor(HttpStatusCode.Gone, body);

        using var response = await client.GetAsync("https://api.example.com/guild/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetMeAsync_Gone_WithClientOutdated_ThrowsClientOutdated()
    {
        var client = new StewardClient(
            ClientFor(HttpStatusCode.Gone, """{"error":"client_outdated","message":"Retired."}"""),
            "https://api.example.com/guild",
            "Steward/1.0.0 (dev)");

        var ex = await Assert.ThrowsAsync<ClientOutdatedException>(() => client.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Retired.", ex.Message);
    }
}
