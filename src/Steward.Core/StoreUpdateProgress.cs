using System.Globalization;

namespace Steward.Core;

public enum AppUpdatePhase
{
    None,
    Pending,
    Downloading,
    Installing,
    Failed,
}

public sealed record StoreUpdateProgress(AppUpdatePhase Phase, double Percent, string Text)
{
    public const double DownloadShare = 0.8;

    public static StoreUpdateProgress None { get; } = new(AppUpdatePhase.None, 0, string.Empty);

    public bool IsActive => Phase is AppUpdatePhase.Pending or AppUpdatePhase.Downloading or AppUpdatePhase.Installing;

    public bool IsIndeterminate => Phase is not AppUpdatePhase.Downloading;

    public static StoreUpdateProgress From(AppUpdatePhase phase, double packageDownloadProgress, ulong bytesDownloaded, ulong totalBytes)
    {
        if (phase is AppUpdatePhase.Downloading && packageDownloadProgress >= DownloadShare)
        {
            phase = AppUpdatePhase.Installing;
        }

        return phase switch
        {
            AppUpdatePhase.Pending => new(phase, 0, "Waiting for the Microsoft Store…"),
            AppUpdatePhase.Downloading => Downloading(packageDownloadProgress, bytesDownloaded, totalBytes),
            AppUpdatePhase.Installing => new(phase, 100, "Installing update… Steward will restart"),
            _ => new(phase, 0, string.Empty),
        };
    }

    private static StoreUpdateProgress Downloading(double packageDownloadProgress, ulong bytesDownloaded, ulong totalBytes)
    {
        var percent = Math.Clamp(Math.Floor(packageDownloadProgress / DownloadShare * 100), 0, 100);
        var text = totalBytes == 0
            ? $"Downloading update… {percent:0}%"
            : $"Downloading update… {percent:0}% ({Megabytes(bytesDownloaded)} of {Megabytes(totalBytes)} MB)";
        return new(AppUpdatePhase.Downloading, percent, text);
    }

    public static string Megabytes(ulong bytes) =>
        (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture);
}
