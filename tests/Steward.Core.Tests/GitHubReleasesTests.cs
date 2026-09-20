using System.Globalization;
using System.Net;

namespace Steward.Core.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class GitHubReleasesFixture
{
    public const string CollectionName = "GitHubReleases static state";
}

[Collection(GitHubReleasesFixture.CollectionName)]
public sealed class GitHubReleasesTests : IDisposable
{
    public GitHubReleasesTests()
    {
        GitHubReleases.ResetCache();
        GitHubReleases.ResetRateLimitForTests();
    }

    public void Dispose()
    {
        GitHubReleases.ResetCache();
        GitHubReleases.ResetRateLimitForTests();
    }

    private sealed class CountingStubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage RateLimited(HttpStatusCode status, int remaining, string? retryAfter = null, long? resetEpochSeconds = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("{}") };
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", remaining.ToString(CultureInfo.InvariantCulture));
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("retry-after", retryAfter);
        }

        if (resetEpochSeconds is { } reset)
        {
            response.Headers.TryAddWithoutValidation("x-ratelimit-reset", reset.ToString(CultureInfo.InvariantCulture));
        }

        return response;
    }

    private static Task<AddonRelease?> FetchAsync(HttpClient httpClient, CancellationToken cancellationToken = default) =>
        GitHubReleases.GetLatestAsync(httpClient, "hoobio/steward-companion", ".msi", "release", cancellationToken);

    [Fact]
    public async Task RateLimited_WithFutureResetHeader_ThrowsWithResetTimeAndMessage()
    {
        var resetAt = DateTimeOffset.UtcNow.AddMinutes(23).AddSeconds(20);
        var handler = new CountingStubHandler(() => RateLimited(HttpStatusCode.Forbidden, remaining: 0, resetEpochSeconds: resetAt.ToUnixTimeSeconds()));
        var httpClient = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<GitHubRateLimitedException>(() => FetchAsync(httpClient, TestContext.Current.CancellationToken));

        Assert.Equal(resetAt.ToUnixTimeSeconds(), ex.ResetAt!.Value.ToUnixTimeSeconds());
        Assert.Contains("GitHub is rate limiting Steward", ex.Message);
        Assert.Contains("in 23 minutes", ex.Message);
        Assert.Contains(resetAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture), ex.Message);
    }

    [Fact]
    public async Task RateLimited_WithRetryAfter_PrefersItOverResetHeader()
    {
        var farAwayReset = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var handler = new CountingStubHandler(() => RateLimited(HttpStatusCode.Forbidden, remaining: 0, retryAfter: "1400", resetEpochSeconds: farAwayReset));
        var httpClient = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<GitHubRateLimitedException>(() => FetchAsync(httpClient, TestContext.Current.CancellationToken));

        Assert.NotNull(ex.ResetAt);
        Assert.InRange(ex.ResetAt!.Value.ToUnixTimeSeconds(), DateTimeOffset.UtcNow.AddSeconds(1395).ToUnixTimeSeconds(), DateTimeOffset.UtcNow.AddSeconds(1405).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task RateLimited_WithNoTimingHeaders_ThrowsWithNullResetAndNoTimingInMessage()
    {
        var handler = new CountingStubHandler(() => RateLimited(HttpStatusCode.Forbidden, remaining: 0));
        var httpClient = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<GitHubRateLimitedException>(() => FetchAsync(httpClient, TestContext.Current.CancellationToken));

        Assert.Null(ex.ResetAt);
        Assert.Equal("GitHub is rate limiting Steward. Unauthenticated checks are capped at 60 an hour per IP.", ex.Message);
    }

    [Fact]
    public async Task Forbidden_WithRemainingQuota_IsNotTreatedAsRateLimit()
    {
        var handler = new CountingStubHandler(() => RateLimited(HttpStatusCode.Forbidden, remaining: 5));
        var httpClient = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(httpClient, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RateLimited_SuppressesFurtherCalls_UntilResetPasses()
    {
        var resetAt = DateTimeOffset.UtcNow.AddSeconds(2);
        var handler = new CountingStubHandler(() => resetAt <= DateTimeOffset.UtcNow
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }
            : RateLimited(HttpStatusCode.Forbidden, remaining: 0, resetEpochSeconds: resetAt.ToUnixTimeSeconds()));
        var httpClient = new HttpClient(handler);

        await Assert.ThrowsAsync<GitHubRateLimitedException>(() => FetchAsync(httpClient, TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Calls);

        await Assert.ThrowsAsync<GitHubRateLimitedException>(() => FetchAsync(httpClient, TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Calls);

        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        var release = await FetchAsync(httpClient, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Calls);
        Assert.Null(release);
    }
}
