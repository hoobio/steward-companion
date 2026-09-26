using System.Diagnostics;
using System.Net;
using System.Text;

namespace Steward.Core;

public sealed class AppUpdater(HttpClient httpClient, string manifestBaseUrl, string storeProductId)
{
    public Uri StoreListingUri { get; } = new($"ms-windows-store://pdp/?productid={storeProductId}");

    public async Task<AddonRelease?> CheckAsync(Version current, string installedVersion, string channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        // System.Version treats an absent component as -1, so 0.4.0 would compare below 0.4.0.0 without this.
        var installed = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));

        if (channel == "release")
        {
            var stable = await LatestAsync("release", cancellationToken).ConfigureAwait(false);
            if (stable is null)
            {
                return null;
            }

            // A pre-release build moves to the latest release whatever its number, since switching channels means leaving pre-release builds behind; the MSI allows the downgrade.
            if (installedVersion.Contains('-', StringComparison.Ordinal))
            {
                return string.Equals(stable.Version.TrimStart('v', 'V'), installedVersion, StringComparison.OrdinalIgnoreCase) ? null : stable;
            }

            return Version.TryParse(stable.Version.TrimStart('v', 'V'), out var available) && available > installed ? stable : null;
        }

        if (channel != "pre-release")
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Steward has release and pre-release channels only.");
        }

        var latestRelease = await LatestAsync("release", cancellationToken).ConfigureAwait(false);
        var latestPreRelease = await LatestAsync("pre-release", cancellationToken).ConfigureAwait(false);
        var newest = (latestRelease, latestPreRelease) switch
        {
            (null, null) => null,
            ({ } r, null) => r,
            (null, { } p) => p,
            ({ } r, { } p) => p.Released > r.Released ? p : r,
        };

        if (newest is null)
        {
            return null;
        }

        var tag = newest.Version.TrimStart('v', 'V');
        if (string.Equals(tag, installedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var numeric = tag.Split('-', 2)[0];
        // A prerelease cut towards an older number than the running build is stale, never an update.
        return Version.TryParse(numeric, out var newestNumeric) && newestNumeric >= installed ? newest : null;
    }

    private async Task<AddonRelease?> LatestAsync(string channel, CancellationToken cancellationToken)
    {
        var manifestUri = new Uri(new Uri(manifestBaseUrl), $"latest-{channel}.json");
        try
        {
            return await AddonUpdater.FetchManifestAsync(httpClient, manifestUri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> DownloadAsync(AddonRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        var msiPath = Path.Combine(Path.GetTempPath(), $"Steward-{release.Version}.msi");
        await AddonUpdater.DownloadAsync(httpClient, new Uri(release.Zip), msiPath, release.Size, progress, cancellationToken).ConfigureAwait(false);
        await AddonUpdater.VerifyChecksumAsync(msiPath, release.Sha256, cancellationToken).ConfigureAwait(false);
        return msiPath;
    }

    public static void InstallAfterExit(string msiPath, string logPath) => RunAfterExit($"""
        Start-Process msiexec.exe -ArgumentList '/i', '"{msiPath}"', '/qn', '/l*v', '"{logPath}"' -Wait
        Remove-Item -LiteralPath '{msiPath}' -ErrorAction SilentlyContinue
        """);

    public static void SwitchToStoreAfterExit(string upgradeCode, string logPath, string appUserModelId) =>
        RunAfterExit(SwitchToStoreScript(upgradeCode, logPath, appUserModelId));

    public static string SwitchToStoreScript(string upgradeCode, string logPath, string appUserModelId) => $$"""
        foreach ($productCode in (New-Object -ComObject WindowsInstaller.Installer).RelatedProducts('{{upgradeCode}}')) {
            Start-Process msiexec.exe -ArgumentList '/x', $productCode, '/qn', '/l*v', '"{{logPath}}"' -Wait
        }
        Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Steward' -ErrorAction SilentlyContinue
        Start-Process explorer.exe -ArgumentList 'shell:AppsFolder\{{appUserModelId}}'
        """;

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
