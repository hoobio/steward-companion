namespace Steward.Core.Tests;

public sealed class StartOnLoginTests
{
    private static readonly string Root = Path.GetTempPath();

    [Fact]
    public void Paths_AreUnderTheGivenFolders()
    {
        Assert.Equal(Path.Combine(Root, "cfg", "autostart", "steward.desktop"), StartOnLogin.LinuxAutostartPath(Path.Combine(Root, "cfg"), "home"));
        Assert.Equal(Path.Combine("home", "Library", "LaunchAgents", "io.hoobi.steward.plist"), StartOnLogin.MacLaunchAgentPath("home"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    public void LinuxAutostartPath_IgnoresAnUnsetOrRelativeConfigHome(string? xdgConfigHome) =>
        Assert.Equal(Path.Combine("home", ".config", "autostart", "steward.desktop"), StartOnLogin.LinuxAutostartPath(xdgConfigHome, "home"));

    [Fact]
    public void LinuxDesktopEntry_StartsInTheTray()
    {
        var entry = StartOnLogin.LinuxDesktopEntry("/opt/steward/Steward.Desktop");

        Assert.StartsWith("[Desktop Entry]\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nType=Application\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nExec=\"/opt/steward/Steward.Desktop\" --tray\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nX-GNOME-Autostart-enabled=true\n", entry, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', entry);
    }

    [Fact]
    public void LinuxDesktopEntry_EscapesTheExecPath()
    {
        var entry = StartOnLogin.LinuxDesktopEntry("/home/a b/50%/$x\"q\\y");

        Assert.Contains("\nExec=\"/home/a b/50%%/\\\\$x\\\\\"q\\\\\\\\y\" --tray\n", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void MacLaunchAgent_RunsAtLoadWithTheTrayArgument()
    {
        var plist = StartOnLogin.MacLaunchAgent("/Applications/Steward & Co.app/Contents/MacOS/Steward");

        Assert.Contains("<string>io.hoobi.steward</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>/Applications/Steward &amp; Co.app/Contents/MacOS/Steward</string>\n        <string>--tray</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>RunAtLoad</key>\n    <true/>", plist, StringComparison.Ordinal);
    }
}
