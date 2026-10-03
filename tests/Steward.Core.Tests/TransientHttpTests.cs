using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Steward.Core.Tests;

public sealed class TransientHttpTests
{
    private const string MeBody = """{"user":{"id":"1","name":"Hoobi"},"guilds":[]}""";

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string Body { get; init; } = MeBody;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Calls, statuses.Length - 1)];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ThrowingHandler(int failures, Exception failure) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            ++Calls <= failures
                ? Task.FromException<HttpResponseMessage>(failure)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MeBody, Encoding.UTF8, "application/json") });
    }

    private static readonly TimeSpan[] NoDelay = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    private static StewardClient ClientOver(HttpMessageHandler inner) =>
        new(new HttpClient(new TransientRetryHandler(NoDelay) { InnerHandler = inner }), "https://api.example.com/guild", "Steward/1.0.0 (dev)");

    [Fact]
    public void Backoff_FollowsTheScheduleThenHoldsAtTheCap()
    {
        int[] expected = [1, 2, 4, 8, 16, 30, 30, 30];
        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(attempt => (int)TransientHttp.Backoff(attempt).TotalSeconds));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void IsTransient_ByStatus(HttpStatusCode status, bool transient)
    {
        Assert.Equal(transient, TransientHttp.IsTransient(status));
        Assert.Equal(transient, TransientHttp.IsTransient(new HttpRequestException("x", null, status)));
        Assert.Equal(transient, TransientHttp.IsTransient(new StewardRequestException(status, null)));
    }

    [Fact]
    public void IsTransient_NetworkFailures()
    {
        Assert.True(TransientHttp.IsTransient(new HttpRequestException(HttpRequestError.ConnectionError)));
        Assert.True(TransientHttp.IsTransient(new HttpRequestException(HttpRequestError.NameResolutionError)));
        Assert.True(TransientHttp.IsTransient(new HttpIOException(HttpRequestError.ResponseEnded)));
        Assert.True(TransientHttp.IsTransient(new TaskCanceledException("timeout", new TimeoutException())));
        Assert.True(TransientHttp.IsTransient(new TimeoutException()));
    }

    [Fact]
    public void IsTransient_NotForAuthOrOutdatedOrCancellation()
    {
        Assert.False(TransientHttp.IsTransient(new SessionExpiredException()));
        Assert.False(TransientHttp.IsTransient(new ClientOutdatedException("old")));
        Assert.False(TransientHttp.IsTransient(new TaskCanceledException()));
        Assert.False(TransientHttp.IsTransient(new HttpRequestException("empty body")));
    }

    [Fact]
    public void RetryAfter_ReadsTheDeltaCappedAtTheMaximumBackoff()
    {
        using var short_ = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        short_.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        using var long_ = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        long_.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
        using var none = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Assert.Equal(TimeSpan.FromSeconds(3), TransientHttp.RetryAfter(short_));
        Assert.Equal(TransientHttp.MaxBackoff, TransientHttp.RetryAfter(long_));
        Assert.Null(TransientHttp.RetryAfter(none));
    }

    [Fact]
    public async Task GetMeAsync_ThreeBadGatewaysThenOk_Succeeds()
    {
        var inner = new SequenceHandler(HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.OK);

        var me = await ClientOver(inner).GetMeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Hoobi", me.User.Name);
        Assert.Equal(4, inner.Calls);
    }

    [Fact]
    public async Task GetMeAsync_ConnectionRefusedTwiceThenOk_Succeeds()
    {
        var inner = new ThrowingHandler(2, new HttpRequestException(HttpRequestError.ConnectionError));

        var me = await ClientOver(inner).GetMeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Hoobi", me.User.Name);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task GetMeAsync_StillFailingAfterTheSchedule_ThrowsWithTheStatus()
    {
        var inner = new SequenceHandler(HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ClientOver(inner).GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(NoDelay.Length + 1, inner.Calls);
    }

    [Fact]
    public async Task GetMeAsync_Unauthorized_IsNotRetried()
    {
        var inner = new SequenceHandler(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<SessionExpiredException>(() => ClientOver(inner).GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task StreamAccessEventsAsync_BadGatewaysThenOk_ConnectsAndYieldsEvents()
    {
        var inner = new SequenceHandler(HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.OK)
        {
            Body = "data: {\"type\": \"connected\"}\n\n",
        };

        var events = new List<string>();
        await foreach (var eventType in ClientOver(inner).StreamAccessEventsAsync(TestContext.Current.CancellationToken))
        {
            events.Add(eventType);
        }

        Assert.Equal(["connected"], events);
        Assert.Equal(4, inner.Calls);
    }

    [Fact]
    public async Task PostCharacterSyncAsync_BadGatewayThenOk_RetriesTheSameBatch()
    {
        var inner = new SequenceHandler(HttpStatusCode.BadGateway, HttpStatusCode.OK) { Body = """{"accepted":0,"rejected":[]}""" };

        await ClientOver(inner).PostCharacterSyncAsync("1", new CharacterSyncRequest("batch-1", "1.0.0", []), TestContext.Current.CancellationToken);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Post_WithoutTheIdempotentOption_IsNotRetried()
    {
        var inner = new SequenceHandler(HttpStatusCode.BadGateway, HttpStatusCode.OK);
        using var client = new HttpClient(new TransientRetryHandler(NoDelay) { InnerHandler = inner });

        using var response = await client.PostAsync(new Uri("https://api.example.com/x"), new StringContent("{}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }
}
