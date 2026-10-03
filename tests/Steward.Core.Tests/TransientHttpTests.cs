using System.Net;

namespace Steward.Core.Tests;

public sealed class TransientHttpTests
{
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
}
