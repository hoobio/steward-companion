using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Steward.Core;

public static class GitHubReleases
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset FetchedAt, GitHubRelease[] Releases)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    public static async Task<AddonRelease?> GetLatestAsync(
        HttpClient httpClient, string repo, string assetExtension, string channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var releases = await ListAsync(httpClient, repo, cancellationToken).ConfigureAwait(false);
        var release = channel switch
        {
            "release" => releases.FirstOrDefault(r => !r.Draft && !r.Prerelease),
            "pre-release" => releases.FirstOrDefault(r => !r.Draft && r.Prerelease),
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

    internal static void ResetCache() => Cache.Clear();

    private static async Task<GitHubRelease[]> ListAsync(HttpClient httpClient, string repo, CancellationToken cancellationToken)
    {
        if (Cache.TryGetValue(repo, out var cached) && DateTimeOffset.UtcNow - cached.FetchedAt < CacheFor)
        {
            return cached.Releases;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases?per_page=20");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // GitHub rejects requests without a User-Agent with 403.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Steward", "1"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var releases = await response.Content.ReadFromJsonAsync(CompanionJsonContext.Default.GitHubReleaseArray, cancellationToken).ConfigureAwait(false) ?? [];
        Cache[repo] = (DateTimeOffset.UtcNow, releases);
        return releases;
    }
}
