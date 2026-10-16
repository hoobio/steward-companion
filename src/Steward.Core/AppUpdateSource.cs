namespace Steward.Core;

public enum AppUpdateSource
{
    None,
    Store,
    Msi,
    ReleasePage,
}

public static class AppUpdateSources
{
    public static AppUpdateSource Resolve(bool isWindows, bool isPackaged, bool isGitHubRelease, bool isPreviewZip) =>
        (isGitHubRelease, isWindows, isPackaged, isPreviewZip) switch
        {
            (false, _, _, _) => AppUpdateSource.None,
            (true, _, false, true) => AppUpdateSource.ReleasePage,
            (true, false, _, _) => AppUpdateSource.ReleasePage,
            (true, true, true, _) => AppUpdateSource.Store,
            (true, true, false, _) => AppUpdateSource.Msi,
        };
}
