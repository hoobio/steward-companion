using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.App.Services;

public static class UpdateProgressPreview
{
    public const string Argument = "--update-progress-preview";

    private const ulong Size = 30_513_561;

    public static bool IsRequested(IEnumerable<string> arguments) =>
        arguments.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    public static void Apply(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        main.UserName = ScreenshotPreview.UserName;
        main.IsAuthorized = true;
        main.IsSignedIn = true;
        main.AppUpdate = new AddonRelease(string.Empty, string.Empty, string.Empty, 0, DateTimeOffset.Now);
        main.RescanCommand.Execute(null);
        _ = Task.Run(() => WalkAsync(main));
    }

    private static async Task WalkAsync(MainViewModel main)
    {
        while (true)
        {
            main.ReportStoreProgress(Status(AppUpdatePhase.Pending, 0));
            await Task.Delay(1500).ConfigureAwait(false);
            for (var step = 0; step <= 100; step++)
            {
                main.ReportStoreProgress(Status(AppUpdatePhase.Downloading, StoreUpdateProgress.DownloadShare * step / 100));
                await Task.Delay(70).ConfigureAwait(false);
            }

            main.ReportStoreProgress(Status(AppUpdatePhase.Installing, 0.9));
            await Task.Delay(4000).ConfigureAwait(false);
        }
    }

    private static StoreUpdateProgress Status(AppUpdatePhase phase, double progress) =>
        StoreUpdateProgress.From(phase, progress, (ulong)(Size * Math.Min(1, progress / StoreUpdateProgress.DownloadShare)), Size);
}
