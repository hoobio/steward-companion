using Steward.App.ViewModels;
using Steward.Core;

using Windows.Services.Store;

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

        main.UserName = "Hoobi";
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
            main.ReportStoreProgress(Status(StorePackageUpdateState.Pending, 0));
            await Task.Delay(1500).ConfigureAwait(false);
            for (var step = 0; step <= 100; step++)
            {
                main.ReportStoreProgress(Status(StorePackageUpdateState.Downloading, StoreUpdateProgress.DownloadShare * step / 100));
                await Task.Delay(70).ConfigureAwait(false);
            }

            main.ReportStoreProgress(Status(StorePackageUpdateState.Deploying, 0.9));
            await Task.Delay(4000).ConfigureAwait(false);
        }
    }

    private static StorePackageUpdateStatus Status(StorePackageUpdateState state, double progress) => new()
    {
        PackageFamilyName = App.PackageFamilyName,
        PackageDownloadSizeInBytes = Size,
        PackageBytesDownloaded = (ulong)(Size * Math.Min(1, progress / StoreUpdateProgress.DownloadShare)),
        PackageDownloadProgress = progress,
        TotalDownloadProgress = progress,
        PackageUpdateState = state,
    };
}
