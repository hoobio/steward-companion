using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace Steward.Core;

public static class GitHubReleases
{
    public static async Task<AddonRelease?> GetLatestAsync(
        HttpClient httpClient, string repo, string assetExtension, string channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var release = channel switch
        {
            "release" => await SendAsync(httpClient, $"https://api.github.com/repos/{repo}/releases/latest", CompanionJsonContext.Default.GitHubRelease, cancellationToken)
                .ConfigureAwait(false),
            "pre-release" => (await SendAsync(httpClient, $"https://api.github.com/repos/{repo}/releases?per_page=20", CompanionJsonContext.Default.GitHubReleaseArray, cancellationToken)
                .ConfigureAwait(false))?.FirstOrDefault(r => r.Prerelease && !r.Draft),
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

    private static async Task<T?> SendAsync<T>(HttpClient httpClient, string uri, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // GitHub rejects requests without a User-Agent with 403.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Steward", "1"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);
    }
}
