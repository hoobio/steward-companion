using System.Net;
using System.Net.Sockets;

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

    [Fact]
    public void IsTransient_SocketFailuresInsideAnIOException()
    {
        Assert.True(TransientHttp.IsTransient(new IOException("reset", new SocketException((int)SocketError.ConnectionReset))));
        Assert.True(TransientHttp.IsTransient(new HttpRequestException("x", new IOException("reset"))));
    }

    [Fact]
    public void IsRetryable_NotForFileSystemFailures()
    {
        Assert.False(TransientHttp.IsRetryable(new IOException("Read-only file system")));
        Assert.False(TransientHttp.IsRetryable(new UnauthorizedAccessException("Access to the path is denied.")));
        Assert.False(TransientHttp.IsRetryable(new DirectoryNotFoundException()));
        Assert.False(TransientHttp.IsRetryable(new FileNotFoundException()));
        Assert.False(TransientHttp.IsRetryable(new PathTooLongException()));
    }

    [Fact]
    public void IsRetryable_AnyServerErrorOrTransientFailure()
    {
        Assert.True(TransientHttp.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.InternalServerError)));
        Assert.True(TransientHttp.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable)));
        Assert.True(TransientHttp.IsRetryable(new HttpRequestException(HttpRequestError.ConnectionError)));
        Assert.True(TransientHttp.IsRetryable(new TaskCanceledException("timeout", new TimeoutException())));
        Assert.False(TransientHttp.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.Forbidden)));
        Assert.False(TransientHttp.IsRetryable(new ClientOutdatedException("old")));
        Assert.False(TransientHttp.IsRetryable(new SessionExpiredException()));
        Assert.False(TransientHttp.IsRetryable(new TaskCanceledException()));
    }
}
