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
}
