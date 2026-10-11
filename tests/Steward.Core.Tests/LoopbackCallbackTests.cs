using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Steward.Core.Tests;

public sealed class LoopbackCallbackTests
{
    [Theory]
    [InlineData("GET /?code=abc-DEF_123 HTTP/1.1", true, "abc-DEF_123")]
    [InlineData("GET /favicon.ico HTTP/1.1", false, "")]
    [InlineData("GET /?code= HTTP/1.1", false, "")]
    [InlineData("GET /?code=abc+def HTTP/1.1", false, "")]
    [InlineData(null, false, "")]
    [InlineData("POST /?code=x HTTP/1.1", false, "")]
    public void TryReadCode_MatchesOnlyGetCallbacks(string? requestLine, bool expected, string expectedCode)
    {
        Assert.Equal(expected, LoopbackCallback.TryReadCode(requestLine, out var code));
        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public async Task WaitForTokenAsync_SkipsFaviconAndRejectedCodeThenExchangesTheNextCode()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var exchanged = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var wait = LoopbackCallback.WaitForTokenAsync(
            listener,
            "ok",
            (code, _) =>
            {
                exchanged.Add(code);
                return code == "fromSecondTab"
                    ? Task.FromResult("token")
                    : Task.FromException<string>(new InvalidOperationException("rejected"));
            },
            timeout.Token);

        foreach (var path in new[] { "/favicon.ico", "/?code=fromFirstTab", "/?code=fromSecondTab" })
        {
            Assert.StartsWith("HTTP/1.1 200 OK", await GetAsync(port, path, timeout.Token));
        }

        Assert.Equal("token", await wait);
        Assert.Equal(["fromFirstTab", "fromSecondTab"], exchanged);
    }

    [Theory]
    [InlineData("GET /?error=not_member HTTP/1.1", true, "not_member")]
    [InlineData("GET /?state=x&error=not_member HTTP/1.1", true, "not_member")]
    [InlineData("GET /?error=access_denied&x=1 HTTP/1.1", true, "access_denied")]
    [InlineData("GET /?code=abc HTTP/1.1", false, "")]
    [InlineData("GET /favicon.ico?error=not_member HTTP/1.1", false, "")]
    [InlineData("POST /?error=not_member HTTP/1.1", false, "")]
    [InlineData(null, false, "")]
    public void TryReadError_MatchesOnlyGetCallbacks(string? requestLine, bool expected, string expectedError)
    {
        Assert.Equal(expected, LoopbackCallback.TryReadError(requestLine, out var error));
        Assert.Equal(expectedError, error);
    }

    private static (TcpListener Listener, int Port) Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    [Fact]
    public async Task WaitForTokenAsync_NotMember_StopsAtOnceWithTheRefusal()
    {
        var (listener, port) = Listen();
        using var _ = listener;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var exchanged = 0;

        var wait = LoopbackCallback.WaitForTokenAsync(listener, "ok", (_, _) => { exchanged++; return Task.FromResult("token"); }, timeout.Token);
        await GetAsync(port, "/?error=not_member", timeout.Token);

        var ex = await Assert.ThrowsAsync<SignInRefusedException>(() => wait);
        Assert.Equal("You are not a member of a Steward server", ex.Message);
        Assert.Equal(0, exchanged);
        Assert.False(timeout.IsCancellationRequested);
    }

    [Fact]
    public async Task WaitForTokenAsync_StrayRequestsAndOtherErrors_KeepListening()
    {
        var (listener, port) = Listen();
        using var _ = listener;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var wait = LoopbackCallback.WaitForTokenAsync(listener, "ok", (code, _) => Task.FromResult($"token-{code}"), timeout.Token);
        foreach (var path in new[] { "/", "/?state=1", "/?error=access_denied", "/?code=real" })
        {
            await GetAsync(port, path, timeout.Token);
        }

        Assert.Equal("token-real", await wait);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("rejected")]
    [InlineData("json")]
    public async Task WaitForTokenAsync_FailedExchange_KeepsListeningForTheRealCode(string failure)
    {
        var (listener, port) = Listen();
        using var _ = listener;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var wait = LoopbackCallback.WaitForTokenAsync(
            listener,
            "ok",
            (code, _) => code == "bad"
                ? Task.FromException<string>(failure switch
                {
                    "http" => new HttpRequestException("502", null, HttpStatusCode.BadGateway),
                    "rejected" => new InvalidOperationException("rejected"),
                    _ => new System.Text.Json.JsonException("not json"),
                })
                : Task.FromResult("token"),
            timeout.Token);
        await GetAsync(port, "/?code=bad", timeout.Token);
        await GetAsync(port, "/?code=good", timeout.Token);

        Assert.Equal("token", await wait);
    }

    [Fact]
    public async Task WaitForTokenAsync_ClientOutdatedExchange_EndsTheWait()
    {
        var (listener, port) = Listen();
        using var _ = listener;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var wait = LoopbackCallback.WaitForTokenAsync(listener, "ok", (_, _) => Task.FromException<string>(new ClientOutdatedException("old")), timeout.Token);
        await GetAsync(port, "/?code=x", timeout.Token);

        await Assert.ThrowsAsync<ClientOutdatedException>(() => wait);
    }

    private sealed class ExchangeStub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static StewardClient ExchangeClient(HttpStatusCode status, string body) =>
        new(new HttpClient(new ExchangeStub(status, body)), "https://api.example.com/guild", "Steward/1.0.0 (dev)");

    [Fact]
    public async Task WaitForTokenAsync_NotMemberExchange_StopsAtOnceWithTheRefusal()
    {
        var (listener, port) = Listen();
        using var _ = listener;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = ExchangeClient(HttpStatusCode.Forbidden, """{"error": "not_member"}""");

        var wait = LoopbackCallback.WaitForTokenAsync(listener, "ok", (code, ct) => client.ExchangeDesktopCodeAsync(code, "verifier", ct), timeout.Token);
        await GetAsync(port, "/?code=x", timeout.Token);

        var ex = await Assert.ThrowsAsync<SignInRefusedException>(() => wait);
        Assert.Equal("You are not a member of a Steward server", ex.Message);
        Assert.False(timeout.IsCancellationRequested);
    }

    [Theory]
    [InlineData("""{"error": "forbidden"}""")]
    [InlineData("not_member")]
    [InlineData("")]
    public async Task ExchangeDesktopCodeAsync_OtherForbidden_IsAnHttpFailure(string body)
    {
        var client = ExchangeClient(HttpStatusCode.Forbidden, body);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.ExchangeDesktopCodeAsync("x", "verifier", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task WaitForTokenAsync_SilentConnection_DoesNotStallTheListener()
    {
        var (listener, port) = Listen();
        using var _ = listener;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var wait = LoopbackCallback.WaitForTokenAsync(listener, "ok", (_, _) => Task.FromResult("token"), timeout.Token, TimeSpan.FromMilliseconds(50));
        using var silent = new TcpClient();
        await silent.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        await GetAsync(port, "/?code=x", timeout.Token);

        Assert.Equal("token", await wait);
    }

    private static async Task<string> GetAsync(int port, string path, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\n"), cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
