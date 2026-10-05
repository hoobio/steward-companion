using System.Diagnostics;
using System.Net;
using System.Text;

namespace Steward.Core;

public sealed class AppUpdater(HttpClient httpClient, Uri msiManifestUri, string storeProductId)
{
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

        var msiPath = Path.Combine(Path.GetTempPath(), $"Steward-{release.Version.TrimStart('v', 'V')}.msi");
        var msiUri = release.Zip is null
            ? throw new InvalidOperationException($"The Steward {release.Version} manifest has no MSI")
            : new Uri(msiManifestUri, release.Zip);
        await AddonUpdater.DownloadAsync(httpClient, msiUri, msiPath, release.Size, progress, cancellationToken).ConfigureAwait(false);
        await AddonUpdater.VerifyChecksumAsync(msiPath, release.Sha256, null, cancellationToken).ConfigureAwait(false);
        return msiPath;
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
