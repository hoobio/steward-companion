using System.Diagnostics;
using System.Text;

namespace Steward.Core;

public sealed class AppUpdater(HttpClient httpClient, string repo)
{
    public async Task<AddonRelease?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        var release = await GitHubReleases.GetLatestAsync(httpClient, repo, ".msi", cancellationToken).ConfigureAwait(false);
        if (release is null || !Version.TryParse(release.Version.TrimStart('v', 'V'), out var available))
        {
            return null;
        }

        // System.Version treats an absent component as -1, so 0.4.0 would compare below 0.4.0.0 without this.
        var installed = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        return available > installed ? release : null;
    }

    public async Task<string> DownloadAsync(AddonRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        var msiPath = Path.Combine(Path.GetTempPath(), $"Steward-{release.Version}.msi");
        await AddonUpdater.DownloadAsync(httpClient, new Uri(release.Zip), msiPath, release.Size, progress, cancellationToken).ConfigureAwait(false);
        await AddonUpdater.VerifyChecksumAsync(msiPath, release.Sha256, cancellationToken).ConfigureAwait(false);
        return msiPath;
    }

    public static void InstallAfterExit(string msiPath, string relaunchPath, string logPath)
    {
        var script = $"""
            Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue
            Start-Process msiexec.exe -ArgumentList '/i', '"{msiPath}"', '/qn', '/l*v', '"{logPath}"' -Wait
            Remove-Item -LiteralPath '{msiPath}' -ErrorAction SilentlyContinue
            Start-Process -FilePath '{relaunchPath}'
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }
}
