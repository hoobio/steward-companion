using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Steward.Core;

public static class GitHubReleases
{
    public static async Task<AddonRelease?> GetLatestAsync(
        HttpClient httpClient, string repo, string assetExtension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // GitHub rejects requests without a User-Agent with 403.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Steward", "1"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var release = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.GitHubRelease, cancellationToken)
            .ConfigureAwait(false);
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
}
