using System.Security;
using System.Text;

namespace Steward.Core;

public static class StartOnLogin
{
    public const string TrayArgument = "--tray";
    public const string LaunchAgentLabel = "io.hoobi.steward";

    public static string LinuxAutostartPath(string? xdgConfigHome, string home) =>
        Path.Combine(Path.IsPathRooted(xdgConfigHome) ? xdgConfigHome : Path.Combine(home, ".config"), "autostart", "steward.desktop");

    public static string MacLaunchAgentPath(string home) => Path.Combine(home, "Library", "LaunchAgents", LaunchAgentLabel + ".plist");

    public static string LinuxDesktopEntry(string exePath) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name=Steward
        Exec={ExecArgument(exePath)} {TrayArgument}
        Terminal=false
        X-GNOME-Autostart-enabled=true

        """.ReplaceLineEndings("\n");

    public static string MacLaunchAgent(string exePath) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{LaunchAgentLabel}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{SecurityElement.Escape(exePath)}</string>
                <string>{TrayArgument}</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
        </dict>
        </plist>

        """.ReplaceLineEndings("\n");

    // The Desktop Entry spec escapes inside the quoted argument first, then escapes backslashes again for the key file.
    private static string ExecArgument(string path)
    {
        var quoted = new StringBuilder("\"");
        foreach (var c in path)
        {
            if (c is '"' or '`' or '$' or '\\')
            {
                quoted.Append('\\');
            }

            quoted.Append(c);
        }

        return quoted.Append('"').ToString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);
    }
}
