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
    // The Avalonia build has no Store package or MSI of its own yet, so installing either would replace it with the WinUI app.
    public static AppUpdateSource Resolve(bool isWindows, bool isPackaged, bool isGitHubRelease, bool isAvalonia) =>
        (isGitHubRelease, isWindows && !isAvalonia, isPackaged) switch
        {
            (false, _, _) => AppUpdateSource.None,
            (true, false, _) => AppUpdateSource.ReleasePage,
            (true, true, true) => AppUpdateSource.Store,
            (true, true, false) => AppUpdateSource.Msi,
        };
}
