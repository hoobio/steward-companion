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

    public string StoreProductId { get; } = storeProductId;

    public Uri StoreListingUri { get; } = new($"ms-windows-store://pdp/?productid={storeProductId}");

    public Uri StoreUpdatesUri { get; } = new("ms-windows-store://downloadsandupdates");

    public async Task<AddonRelease?> CheckMsiAsync(string installedVersion, CancellationToken cancellationToken)
    {
        try
        {
            var release = await AddonUpdater.FetchManifestAsync(httpClient, msiManifestUri, cancellationToken).ConfigureAwait(false);
            return release is not null && IsNewer(release.Version, installedVersion) ? release : null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public static bool IsNewer(string available, string installed) =>
        ParseVersion(available) is { } a && ParseVersion(installed) is { } i && a > i;

    private static Version? ParseVersion(string value)
    {
        var numeric = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        // System.Version reads an absent component as -1, so 0.20.2 would sort below 0.20.2.0 without padding.
        return Version.TryParse(numeric, out var parsed)
            ? new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0))
            : null;
    }

    public async Task<string> DownloadMsiAsync(AddonRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (!StrictVersion().IsMatch(release.Version))
        {
            throw new InvalidOperationException($"The Steward manifest version '{release.Version}' is not a version number; refusing to download");
        }

        var msiPath = Path.Combine(Path.GetTempPath(), $"Steward-{release.Version.TrimStart('v', 'V')}.msi");
        var msiUri = release.Zip is null
            ? throw new InvalidOperationException($"The Steward {release.Version} manifest has no MSI")
            : new Uri(msiManifestUri, release.Zip);
        var (tag, file) = ReleaseAsset(msiUri)
            ?? throw new UpdateVerificationException($"{msiUri} is not an MSI on a hoobio/steward-companion GitHub release");
        if (!string.Equals(tag, release.Version, StringComparison.Ordinal))
        {
            throw new UpdateVerificationException($"the MSI is from release {tag}, not {release.Version}");
        }

        if (release.Sha256 is null)
        {
            throw new UpdateVerificationException($"the {release.Version} manifest has no sha256");
        }

        var digest = await GitHubDigestAsync(tag, file, msiUri, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(digest, release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateVerificationException($"GitHub's sha256 for {file} does not match the manifest");
        }

        await AddonUpdater.DownloadAsync(httpClient, msiUri, msiPath, release.Size, progress, cancellationToken).ConfigureAwait(false);
        try
        {
            await AddonUpdater.VerifyChecksumAsync(msiPath, digest, null, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new UpdateVerificationException($"the downloaded MSI does not match GitHub's sha256 ({ex.Message})");
        }

        return msiPath;
    }

    [GeneratedRegex(@"^https://github\.com/hoobio/steward-companion/releases/download/(?<tag>[0-9A-Za-z.+-]+)/(?<file>[0-9A-Za-z._+-]+\.msi)$")]
    private static partial Regex ReleaseMsiUrl();

    internal static (string Tag, string File)? ReleaseAsset(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.UserInfo.Length > 0 || !uri.IsDefaultPort || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return null;
        }

        var match = ReleaseMsiUrl().Match(uri.AbsoluteUri);
        return match.Success ? (match.Groups["tag"].Value, match.Groups["file"].Value) : null;
    }

    private async Task<string> GitHubDigestAsync(string tag, string file, Uri msiUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(GitHubReleaseApi, Uri.EscapeDataString(tag)));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("Steward-companion");
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
