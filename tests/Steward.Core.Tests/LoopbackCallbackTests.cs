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
