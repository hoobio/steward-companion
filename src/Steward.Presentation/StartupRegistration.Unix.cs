using Steward.Core;

namespace Steward.App;

internal static partial class StartupRegistration
{
    private static string EntryPath => OperatingSystem.IsMacOS()
        ? StartOnLogin.MacLaunchAgentPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        : StartOnLogin.LinuxAutostartPath(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static bool IsEnabled() => File.Exists(EntryPath);

    // launchctl is never called: loading a RunAtLoad agent starts a second Steward, and removing a loaded one stops this one.
    public static void Set(bool enabled)
    {
        var path = EntryPath;
        if (!enabled)
        {
            File.Delete(path);
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The Steward executable path is unknown");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, OperatingSystem.IsMacOS() ? StartOnLogin.MacLaunchAgent(exe) : StartOnLogin.LinuxDesktopEntry(exe));
    }
}
