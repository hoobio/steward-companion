using Steward.Core;
using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Steward.App.Services;

public sealed class WindowsStore(ILogger logger) : IDisposable
{
    private bool _logged;

    public bool StartupTaskUnavailable => true;

    public bool HasUpdates => false;

    public Task ScanForUpdatesAsync(string productId, bool scanAllApps, nint ownerWindow)
    {
        LogUnavailable();
        return Task.CompletedTask;
    }

    public Task<StoreUpdateCheck?> CheckUpdatesAsync(nint ownerWindow)
    {
        LogUnavailable();
        return Task.FromResult<StoreUpdateCheck?>(null);
    }

    public Task<StoreInstallOutcome> InstallAsync(bool silently, Action<StoreUpdateProgress> report) =>
        throw new InvalidOperationException("No Microsoft Store update has been found.");

    public Task WatchQueueAsync(Action<StoreUpdateProgress> report, Action<string, bool, bool> finished) => Task.CompletedTask;

    public void ReleaseQueueItem()
    {
    }

    public Task<StartWithWindowsState?> StartupTaskAsync(bool? enable) => Task.FromResult<StartWithWindowsState?>(null);

    public static int RegisterRestartForStoreUpdate(nint handle) => 0;

    public void Dispose()
    {
    }

    private void LogUnavailable()
    {
        if (!_logged)
        {
            _logged = true;
            logger.Info("The Microsoft Store is not available on this OS; Store update checks are off");
        }
    }
}
