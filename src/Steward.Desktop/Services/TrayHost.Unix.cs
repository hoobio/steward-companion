using Tmds.DBus.Protocol;

namespace Steward.Desktop.Services;

internal static class TrayHost
{
    private const string StatusNotifierWatcher = "org.kde.StatusNotifierWatcher";

    public static bool IsAvailable => !OperatingSystem.IsLinux() || HasStatusNotifierWatcher();

    // Avalonia's Linux TrayIcon is a StatusNotifierItem only, with no XEmbed fallback, so without a watcher on the session bus it never appears.
    private static bool HasStatusNotifierWatcher()
    {
        try
        {
            var names = Task.Run(() => DBusConnection.Session.ListServicesAsync());
            if (names.Wait(TimeSpan.FromSeconds(2)))
            {
                return names.Result.Contains(StatusNotifierWatcher);
            }

            _ = names.ContinueWith(task => task.Exception, TaskScheduler.Default);
            return false;
        }
        catch (AggregateException)
        {
            return false;
        }
    }
}
