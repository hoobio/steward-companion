using System.Text.RegularExpressions;

using Microsoft.Win32;

namespace Steward.Core;

public static partial class BattleNet
{
    private const string MacApp = "/Applications/Battle.net.app";

    // A battlenet:// link leaves a tray-minimised Battle.net hidden; starting the exe again restores its window (verified 14 Oct 2026).
    public static string? Launcher()
    {
        if (OperatingSystem.IsWindows())
        {
            return (Registry.ClassesRoot.OpenSubKey(@"battlenet\shell\open\command")?.GetValue(null) as string)
                ?.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } exe && File.Exists(exe) ? exe : null;
        }

        if (OperatingSystem.IsMacOS())
        {
            return Directory.Exists(MacApp) ? MacApp : null;
        }

        return LutrisLink(WinePrefixes.LutrisConfigs(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable));
    }

    internal static string? LutrisLink(IEnumerable<string> configs) =>
        configs.Any(config => LutrisSlug().Replace(Path.GetFileNameWithoutExtension(config), string.Empty) == "battlenet")
            ? "lutris:rungame/battlenet"
            : null;

    [GeneratedRegex(@"-\d+$")]
    private static partial Regex LutrisSlug();
}
