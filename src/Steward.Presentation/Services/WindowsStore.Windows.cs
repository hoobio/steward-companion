using System.Runtime.InteropServices;

using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;

using Windows.ApplicationModel;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using Windows.Management.Deployment;
using Windows.Services.Store;

namespace Steward.App.Services;

public sealed class WindowsStore(ILogger logger) : IDisposable
{
    private const string StartupTaskId = "StewardStartup";
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int ClassNotAvailable = unchecked((int)0x80040111);

    private StoreContext? _context;
    private IReadOnlyList<StorePackageUpdate>? _updates;
    private StoreQueueItem? _queueItem;
    private Action<StoreUpdateProgress>? _queueProgress;
    private Action<string, bool, bool>? _queueFinished;
    private bool _storeOff;
    private bool _installManagerOff;
    private bool _packageManagerOff;
    private bool _startupTaskOff;

    public bool StartupTaskUnavailable => AppInfo.IsWine || _startupTaskOff;

    public bool HasUpdates => _context is not null && _updates is { Count: > 0 };

    public static StoreUpdateProgress ToProgress(StorePackageUpdateStatus status) =>
        StoreUpdateProgress.From(
            status.PackageUpdateState switch
            {
                StorePackageUpdateState.Pending => AppUpdatePhase.Pending,
                StorePackageUpdateState.Downloading => AppUpdatePhase.Downloading,
                StorePackageUpdateState.Deploying or StorePackageUpdateState.Completed => AppUpdatePhase.Installing,
                StorePackageUpdateState.Canceled => AppUpdatePhase.None,
                _ => AppUpdatePhase.Failed,
            },
            status.PackageDownloadProgress,
            status.PackageBytesDownloaded,
            status.PackageDownloadSizeInBytes);

    public async Task ScanForUpdatesAsync(string productId, bool scanAllApps, nint ownerWindow)
    {
        if (AppInfo.IsWine)
        {
            return;
        }

        try
        {
            if (AppInfo.IsGitHubRelease)
            {
                RegisterRestartForStoreUpdate(ownerWindow);
            }
            else
            {
                LogStoreCopyVersion("before");
            }

            if (Activate("The Store installer", ref _installManagerOff, () => new AppInstallManager()) is not { } manager)
            {
                return;
            }

            // SearchForUpdatesAsync is documented as needing a Microsoft-only private capability, but it runs unelevated from this full-trust process with no capability declared (verified 28 Sep 2026).
            var item = await manager.SearchForUpdatesAsync(productId, string.Empty);
            logger.Info(item is null
                ? "Store scan for Steward: no install item returned (the Store may still queue and install the update)"
                : $"Store scan for Steward: {item.GetCurrentStatus().InstallState}");

            if (scanAllApps)
            {
                await ScanAllAppsForUpdatesAsync(manager).ConfigureAwait(true);
            }

            if (!AppInfo.IsGitHubRelease)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                    LogStoreCopyVersion("2 minutes after");
                });
            }
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Store scan for Steward failed");
        }
    }

    private async Task ScanAllAppsForUpdatesAsync(AppInstallManager manager)
    {
        try
        {
            // Only SearchForAllUpdatesAsync refreshes a flight's catalogue (verified 29 Sep 2026); it also queues every other Store app's update, hence explicit-press only.
            await manager.SearchForAllUpdatesAsync();
        }
        catch (Exception ex)
        {
            // Verified 29 Sep 2026: this call throws (AggregateException, cause uncaptured) yet still queues the update.
            logger.Warn(ex, "Store scan for all apps failed");
        }

        var queued = manager.AppInstallItems.Any(queueItem => queueItem.PackageFamilyName == AppInfo.PackageFamilyName);
        logger.Info($"Store scan for all apps: {manager.AppInstallItems.Count} item(s), Steward {(queued ? "queued" : "not queued")}");
    }

    private void LogStoreCopyVersion(string when)
    {
        if (Activate("The package manager", ref _packageManagerOff, () => new PackageManager()) is not { } packages)
        {
            return;
        }

        var package = packages.FindPackagesForUser(string.Empty, AppInfo.PackageFamilyName).FirstOrDefault();
        logger.Info(package is null
            ? $"Store copy of Steward not installed ({when} the scan)"
            : $"Store copy of Steward is {package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision} ({when} the scan)");
    }

    public async Task<StoreUpdateCheck?> CheckUpdatesAsync(nint ownerWindow)
    {
        if (AppInfo.IsWine || Activate("The Microsoft Store", ref _storeOff, () =>
            {
                var created = StoreContext.GetDefault();
                WinRT.Interop.InitializeWithWindow.Initialize(created, ownerWindow);
                return created;
            }) is not { } context)
        {
            return null;
        }

        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
        _context = context;
        _updates = updates;
        return new StoreUpdateCheck(updates.Count, context.CanSilentlyDownloadStorePackageUpdates);
    }

    public async Task<StoreInstallOutcome> InstallAsync(bool silently, Action<StoreUpdateProgress> report)
    {
        var context = _context ?? throw new InvalidOperationException("No Microsoft Store update has been found.");
        var updates = _updates ?? throw new InvalidOperationException("No Microsoft Store update has been found.");
        var operation = silently
            ? context.TrySilentDownloadAndInstallStorePackageUpdatesAsync(updates)
            : context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
        operation.Progress = (_, status) => report(ToProgress(status));
        var result = await operation;
        return new StoreInstallOutcome(
            result.OverallState.ToString(),
            result.OverallState is StorePackageUpdateState.Completed,
            result.OverallState is StorePackageUpdateState.Canceled);
    }

    public async Task WatchQueueAsync(Action<StoreUpdateProgress> report, Action<string, bool, bool> finished)
    {
        // The finished install's queue item outlives the restart and read as installing forever (29 Sep 2026).
        if (_context is not { } context || _queueItem is not null || _updates is not { Count: > 0 })
        {
            return;
        }

        try
        {
            var items = await context.GetAssociatedStoreQueueItemsAsync();
            if (_queueItem is not null || items.FirstOrDefault(item => item.InstallKind == StoreQueueItemKind.Update) is not { } item)
            {
                return;
            }

            _queueItem = item;
            _queueProgress = report;
            _queueFinished = finished;
            item.StatusChanged += OnQueueItemStatusChanged;
            logger.Info("Store update: following an update the Microsoft Store queued");
            OnQueueItemStatusChanged(item, null);
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Store update queue check failed");
        }
    }

    public void ReleaseQueueItem() => _queueItem = null;

    private void OnQueueItemStatusChanged(StoreQueueItem item, object? args)
    {
        var status = item.GetCurrentStatus();
        var waiting = status.PackageInstallState is StoreQueueItemState.Paused
            || status.PackageInstallState is StoreQueueItemState.Active && status.UpdateStatus.PackageUpdateState is StorePackageUpdateState.Pending;
        // The Store kept a Pending Update item for Steward with no update available to StoreContext (29 Sep 2026).
        if (waiting && _updates is not { Count: > 0 })
        {
            return;
        }

        switch (status.PackageInstallState)
        {
            case StoreQueueItemState.Active:
                _queueProgress?.Invoke(ToProgress(status.UpdateStatus));
                return;
            case StoreQueueItemState.Paused:
                _queueProgress?.Invoke(StoreUpdateProgress.From(AppUpdatePhase.Pending, 0, 0, 0));
                return;
        }

        item.StatusChanged -= OnQueueItemStatusChanged;
        _queueFinished?.Invoke(
            status.PackageInstallExtendedState.ToString(),
            status.PackageInstallState is StoreQueueItemState.Completed,
            status.PackageInstallState is StoreQueueItemState.Error);
    }

    public async Task<StartWithWindowsState?> StartupTaskAsync(bool? enable)
    {
        if (AppInfo.IsWine || _startupTaskOff)
        {
            return null;
        }

        StartupTask task;
        try
        {
            task = await StartupTask.GetAsync(StartupTaskId);
        }
        catch (Exception ex) when (IsActivationFailure(ex))
        {
            TurnOff("Start with Windows", ref _startupTaskOff, ex);
            return null;
        }

        var state = enable switch
        {
            true => await task.RequestEnableAsync(),
            false => DisableStartupTask(task),
            null => task.State,
        };
        return state switch
        {
            StartupTaskState.Enabled => StartWithWindowsState.Enabled,
            StartupTaskState.DisabledByUser => StartWithWindowsState.DisabledByUser,
            StartupTaskState.DisabledByPolicy => StartWithWindowsState.DisabledByPolicy,
            StartupTaskState.EnabledByPolicy => StartWithWindowsState.EnabledByPolicy,
            _ => StartWithWindowsState.Disabled,
        };
    }

    private static StartupTaskState DisableStartupTask(StartupTask task)
    {
        task.Disable();
        return task.State;
    }

    // Windows closes the app to apply a Store update and relaunches it under this registration; neither argument lets the relaunch take focus.
    public static int RegisterRestartForStoreUpdate(nint handle) =>
        RegisterApplicationRestart(
            IsWindowVisible(handle) ? StartupRegistration.NoActivateArgument : StartupRegistration.TrayArgument,
            RestartNoCrashHangReboot);

    private T? Activate<T>(string feature, ref bool off, Func<T> create)
        where T : class
    {
        if (off)
        {
            return null;
        }

        try
        {
            return create();
        }
        catch (Exception ex) when (IsActivationFailure(ex))
        {
            TurnOff(feature, ref off, ex);
            return null;
        }
    }

    private void TurnOff(string feature, ref bool off, Exception ex)
    {
        off = true;
        logger.Warn(ex, $"{feature} is not available on this system; turned off for this run");
    }

    public static bool IsActivationFailure(Exception ex) =>
        ex is TypeLoadException or DllNotFoundException or EntryPointNotFoundException
        || ex is COMException { HResult: ClassNotRegistered or ClassNotAvailable };

    public void Dispose()
    {
        if (_queueItem is not null)
        {
            _queueItem.StatusChanged -= OnQueueItemStatusChanged;
        }
    }

    // RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT: only an update should restart the app, not a crash, hang or reboot.
    private const int RestartNoCrashHangReboot = 1 | 2 | 8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart([MarshalAs(UnmanagedType.LPWStr)] string? pwzCommandline, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);
}
