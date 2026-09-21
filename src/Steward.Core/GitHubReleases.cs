using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Steward.Core;

public sealed class GitHubRateLimitedException(DateTimeOffset? resetAt) : Exception(GitHubRateLimitedException.BuildMessage(resetAt))
{
    public DateTimeOffset? ResetAt { get; } = resetAt;

    private static string BuildMessage(DateTimeOffset? resetAt) =>
        resetAt is { } at
            ? $"GitHub is rate limiting Steward. Unauthenticated checks are capped at 60 an hour per IP, and this one resets {RelativeTime.DescribeUntil(at, DateTimeOffset.Now)} ({at.ToLocalTime():t})."
            : "GitHub is rate limiting Steward. Unauthenticated checks are capped at 60 an hour per IP.";
}

public static class GitHubReleases
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset FetchedAt, GitHubRelease[] Releases)> Cache = new(StringComparer.OrdinalIgnoreCase);
    // The manifest host is polled every minute; GitHub's 60-an-hour unauthenticated cap is why the addons sourced from it are not.
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(60);
    private static DateTimeOffset? _rateLimitedUntil;

    public static async Task<AddonRelease?> GetLatestAsync(
        HttpClient httpClient, string repo, string assetExtension, string channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var releases = await ListAsync(httpClient, repo, cancellationToken).ConfigureAwait(false);
        // The API lists releases in an order that is not published date, so the newest is chosen by date rather than position.
        var release = channel switch
        {
            "release" => releases.Where(r => !r.Draft && !r.Prerelease).MaxBy(r => r.PublishedAt),
            "pre-release" => releases.Where(r => !r.Draft && r.Prerelease).MaxBy(r => r.PublishedAt),
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "GitHub addons only have release and pre-release channels."),
        };

        var asset = release?.Assets.FirstOrDefault(a => a.Name.EndsWith(assetExtension, StringComparison.OrdinalIgnoreCase));
        if (release is null || asset is null)
        {
            return null;
        }

        const string digestPrefix = "sha256:";
        if (asset.Digest is null || !asset.Digest.StartsWith(digestPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"GitHub release {release.TagName} of {repo} has no sha256 digest for {asset.Name}.");
        }

        return new AddonRelease(release.TagName, asset.BrowserDownloadUrl, asset.Digest[digestPrefix.Length..], asset.Size, release.PublishedAt);
    }

    public static void ResetCache() => Cache.Clear();

    internal static void ResetRateLimitForTests() => _rateLimitedUntil = null;

    private static async Task<GitHubRelease[]> ListAsync(HttpClient httpClient, string repo, CancellationToken cancellationToken)
    {
        if (Cache.TryGetValue(repo, out var cached) && DateTimeOffset.UtcNow - cached.FetchedAt < CacheFor)
        {
            return cached.Releases;
        }

        if (_rateLimitedUntil is { } until && DateTimeOffset.UtcNow < until)
        {
            throw new GitHubRateLimitedException(until);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases?per_page=20");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // GitHub rejects requests without a User-Agent with 403.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Steward", "1"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (TryGetRateLimitReset(response, out var resetAt))
        {
            _rateLimitedUntil = resetAt;
            throw new GitHubRateLimitedException(resetAt);
        }

        response.EnsureSuccessStatusCode();

        var releases = await response.Content.ReadFromJsonAsync(CompanionJsonContext.Default.GitHubReleaseArray, cancellationToken).ConfigureAwait(false) ?? [];
        Cache[repo] = (DateTimeOffset.UtcNow, releases);
        return releases;
    }

    private static bool TryGetRateLimitReset(HttpResponseMessage response, out DateTimeOffset? resetAt)
    {
        resetAt = null;
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return false;
        }

        if (!response.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues)
            || !int.TryParse(remainingValues.FirstOrDefault(), out var remaining)
            || remaining != 0)
        {
            return false;
        }

        if (response.Headers.TryGetValues("retry-after", out var retryAfterValues)
            && int.TryParse(retryAfterValues.FirstOrDefault(), out var retryAfterSeconds))
        {
            resetAt = DateTimeOffset.UtcNow.AddSeconds(retryAfterSeconds);
            return true;
        }

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var resetEpochSeconds))
        {
            resetAt = DateTimeOffset.FromUnixTimeSeconds(resetEpochSeconds);
        }

        return true;
    }
}
