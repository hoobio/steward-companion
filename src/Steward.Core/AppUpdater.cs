using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Steward.Core;

public sealed class UpdateVerificationException(string message, Exception? inner = null) : Exception(message, inner);

public sealed partial class AppUpdater(HttpClient httpClient, Uri msiManifestUri, string storeProductId)
{
    [GeneratedRegex(@"^[vV]?\d+(\.\d+){2,3}(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex StrictVersion();

    private readonly ManifestCache _manifestCache = new();

    public string StoreProductId { get; } = storeProductId;

    public Uri StoreListingUri { get; } = new($"ms-windows-store://pdp/?productid={storeProductId}");

    public Uri StoreUpdatesUri { get; } = new("ms-windows-store://downloadsandupdates");

    public async Task<AddonRelease?> CheckMsiAsync(string installedVersion, CancellationToken cancellationToken)
    {
        try
        {
            var release = await AddonUpdater.FetchManifestAsync(httpClient, msiManifestUri, cancellationToken, cache: _manifestCache).ConfigureAwait(false);
            return release is not null && IsNewer(release.Version, installedVersion) ? release : null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public static bool IsNewer(string available, string installed) => AppVersion.IsNewer(available, installed);

    internal static readonly Uri GitHubReleasesApi = new("https://api.github.com/repos/hoobio/steward-companion/releases?per_page=30");

    public async Task<AddonRelease?> CheckReleaseAsync(string installedVersion, string assetSuffix, bool includePreRelease, CancellationToken cancellationToken)
    {
        var cached = _manifestCache.Get(GitHubReleasesApi);
        using var request = GitHubRequest(GitHubReleasesApi);
        if (cached is not null)
        {
            // GitHub does not count a 304 against the 60 unauthenticated requests an hour.
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        AddonRelease? latest;
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            latest = cached.Release;
        }
        else
        {
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            latest = LatestRelease(json, assetSuffix, includePreRelease);
            _manifestCache.Set(GitHubReleasesApi, response.Headers.ETag?.ToString(), latest);
        }

        return latest is not null && IsNewer(latest.Version, installedVersion) ? latest : null;
    }

    // The list is not in version order, and a release counts only when it carries this platform's package.
    internal static AddonRelease? LatestRelease(string json, string assetSuffix, bool includePreRelease)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("GitHub's release list is not an array");
        }

        AddonRelease? latest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (IsTrue(release, "draft") || (IsTrue(release, "prerelease") && !includePreRelease)
                || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                if (ReleaseAssetCandidate(release, asset, assetSuffix) is { } candidate
                    && (latest is null || AppVersion.Compare(candidate.Version, latest.Version) > 0))
                {
                    latest = candidate;
                }
            }
        }

        return latest;
    }

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static AddonRelease? ReleaseAssetCandidate(JsonElement release, JsonElement asset, string assetSuffix)
    {
        if (!asset.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { } name
            || !name.StartsWith("Steward-", StringComparison.Ordinal) || !name.EndsWith(assetSuffix, StringComparison.Ordinal)
            || name.Length <= "Steward-".Length + assetSuffix.Length
            || !asset.TryGetProperty("browser_download_url", out var url) || url.GetString() is not { } zip
            || !asset.TryGetProperty("digest", out var digest) || digest.ValueKind != JsonValueKind.String
            || digest.GetString() is not { } digestValue || !digestValue.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return null;
        }

        var version = name["Steward-".Length..^assetSuffix.Length];
        if (!StrictVersion().IsMatch(version))
        {
            return null;
        }

        var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes) ? bytes : 0;
        var published = release.TryGetProperty("published_at", out var at) && at.TryGetDateTimeOffset(out var when) ? when : DateTimeOffset.MinValue;
        var page = release.TryGetProperty("html_url", out var html) ? html.GetString() : null;
        return new AddonRelease(version, zip, digestValue["sha256:".Length..], size, published, Website: page);
    }

    public async Task<string> DownloadMsiAsync(AddonRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        RequireStrictVersion(release);
        var msiUri = release.Zip is null
            ? throw new InvalidOperationException($"The Steward {release.Version} manifest has no MSI")
            : new Uri(msiManifestUri, release.Zip);
        return await DownloadVerifiedAsync(release, msiUri, ".msi", allowPreviewTag: false, Path.GetTempPath(), progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> DownloadReleaseAssetAsync(AddonRelease release, string extension, string directory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        RequireStrictVersion(release);
        var uri = Uri.TryCreate(release.Zip, UriKind.Absolute, out var absolute)
            ? absolute
            : throw new UpdateVerificationException($"the Steward {release.Version} release has no package URL");
        return await DownloadVerifiedAsync(release, uri, extension, allowPreviewTag: true, directory, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> DownloadVerifiedAsync(
        AddonRelease release, Uri uri, string extension, bool allowPreviewTag, string directory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var kind = extension.TrimStart('.').ToUpperInvariant();
        var (tag, file) = ReleaseAsset(uri, extension)
            ?? throw new UpdateVerificationException($"{uri} is not an {kind} on a hoobio/steward-companion GitHub release");
        if (!TagMatches(tag, release.Version, allowPreviewTag))
        {
            throw new UpdateVerificationException($"the {kind} is from release {tag}, not {release.Version}");
        }

        if (allowPreviewTag && !file.StartsWith($"Steward-{WithoutV(release.Version)}-", StringComparison.Ordinal))
        {
            throw new UpdateVerificationException($"{file} is not the Steward {release.Version} package");
        }

        if (release.Sha256 is null)
        {
            throw new UpdateVerificationException($"the {release.Version} manifest has no sha256");
        }

        var digest = await GitHubDigestAsync(tag, file, uri, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(digest, release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateVerificationException($"GitHub's sha256 for {file} does not match the manifest");
        }

        var path = Path.Combine(directory, allowPreviewTag ? file : $"Steward-{WithoutV(release.Version)}{extension}");
        await AddonUpdater.DownloadAsync(httpClient, uri, path, release.Size, progress, cancellationToken).ConfigureAwait(false);
        try
        {
            await AddonUpdater.VerifyChecksumAsync(path, digest, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new UpdateVerificationException($"the downloaded {kind} does not match GitHub's sha256 ({ex.Message})");
        }

        return path;
    }

    private static void RequireStrictVersion(AddonRelease release)
    {
        if (!StrictVersion().IsMatch(release.Version))
        {
            throw new InvalidOperationException($"The Steward manifest version '{release.Version}' is not a version number; refusing to download");
        }
    }

    private static string WithoutV(string version) =>
        version.StartsWith('v') || version.StartsWith('V') ? version[1..] : version;

    private const string PreviewTagPrefix = "desktop-preview-";

    internal static bool TagMatches(string tag, string version, bool allowPreviewTag) =>
        string.Equals(WithoutV(tag), WithoutV(version), StringComparison.Ordinal)
        || (allowPreviewTag && tag.StartsWith(PreviewTagPrefix, StringComparison.Ordinal)
            && tag.Length > PreviewTagPrefix.Length && tag[PreviewTagPrefix.Length..].All(char.IsAsciiDigit)
            && version.EndsWith($"-preview.{tag[PreviewTagPrefix.Length..]}", StringComparison.Ordinal));

    [GeneratedRegex(@"^https://github\.com/hoobio/steward-companion/releases/download/(?<tag>[0-9A-Za-z.+-]+)/(?<file>[0-9A-Za-z._+-]+)$")]
    private static partial Regex ReleaseAssetUrl();

    internal static (string Tag, string File)? ReleaseAsset(Uri uri, string extension = ".msi")
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.UserInfo.Length > 0 || !uri.IsDefaultPort || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return null;
        }

        var match = ReleaseAssetUrl().Match(uri.AbsoluteUri);
        return match.Success && match.Groups["file"].Value.EndsWith(extension, StringComparison.Ordinal)
            ? (match.Groups["tag"].Value, match.Groups["file"].Value)
            : null;
    }

    private async Task<string> GitHubDigestAsync(string tag, string file, Uri msiUri, CancellationToken cancellationToken)
    {
        using var request = GitHubRequest(new Uri(GitHubReleaseApi, Uri.EscapeDataString(tag)));
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ReadDigest(json, file, msiUri)
                ?? throw new UpdateVerificationException($"GitHub lists no sha256 digest for {file} on {tag}");
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateVerificationException($"could not read release {tag} from GitHub ({ex.Message})", ex);
        }
    }

    internal static readonly Uri GitHubReleaseApi = new("https://api.github.com/repos/hoobio/steward-companion/releases/tags/");

    private static HttpRequestMessage GitHubRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("Steward-companion");
        return request;
    }

    internal static string? ReadDigest(string json, string file, Uri msiUri)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) && name.GetString() == file
                    && asset.TryGetProperty("browser_download_url", out var url) && url.GetString() == msiUri.AbsoluteUri
                    && asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String
                    && digest.GetString() is { } value && value.StartsWith("sha256:", StringComparison.Ordinal)
                    && value.Length == "sha256:".Length + 64)
                {
                    return value["sha256:".Length..];
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string EscapeSingleQuoted(string value) => value.Replace("'", "''");

    public static void InstallMsiAfterExit(string msiPath, string logPath) => RunAfterExit(InstallMsiScript(msiPath, logPath));

    public static string InstallMsiScript(string msiPath, string logPath)
    {
        var escapedMsiPath = EscapeSingleQuoted(msiPath);
        var escapedLogPath = EscapeSingleQuoted(logPath);
        // The MSI's own LaunchSteward action relaunches the app after a /qn install (UILevel 2), so the script does not.
        return $$"""
            $exitCode = (Start-Process msiexec.exe -ArgumentList '/i', '"{{escapedMsiPath}}"', '/qn', '/l*v', '"{{escapedLogPath}}"' -PassThru -Wait).ExitCode
            if ($exitCode -ne 0 -and $exitCode -ne 3010) { exit $exitCode }
            Remove-Item -LiteralPath '{{escapedMsiPath}}' -ErrorAction SilentlyContinue
            """;
    }

    private static void RunAfterExit(string body)
    {
        var script = $"""
            Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue
            {body}
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }
}
