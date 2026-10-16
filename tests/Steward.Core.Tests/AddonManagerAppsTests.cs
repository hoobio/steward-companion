namespace Steward.Core.Tests;

public sealed class AddonManagerAppsTests
{
    [Theory]
    [InlineData("\"C:\\Users\\a\\AppData\\Local\\Programs\\CurseForge\\CurseForge.exe\" --hidden", "C:\\Users\\a\\AppData\\Local\\Programs\\CurseForge\\CurseForge.exe")]
    [InlineData("C:\\Program Files\\WowUp\\WowUp.exe --startup", "C:\\Program Files\\WowUp\\WowUp.exe")]
    [InlineData("%LocalAppData%\\WowUp-CF\\WowUp-CF.exe", "%LocalAppData%\\WowUp-CF\\WowUp-CF.exe")]
    [InlineData("", null)]
    [InlineData("\"", null)]
    public void ExePath_TakesTheExecutableFromARunCommand(string command, string? expected)
    {
        Assert.Equal(expected, AddonManagerApps.ExePath(command));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 2, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 1, 0, 0, 0 }, false)]
    public void IsApproved_ReadsTheStartupApprovedFlag(byte[]? value, bool expected)
    {
        Assert.Equal(expected, AddonManagerApps.IsApproved(value));
    }

    [Theory]
    [InlineData("/home/u/Apps/WowUp-2.24.0.AppImage", "wowup")]
    [InlineData("/home/u/Apps/WowUp-CF-2.24.0-beta.1.AppImage", "wowupcf")]
    [InlineData("/tmp/.mount_WowUpXY/wowup-cf", "wowupcf")]
    [InlineData("/opt/CurseForge/curseforge", "curseforge")]
    [InlineData("CurseForge", "curseforge")]
    [InlineData("WowUp-CF", "wowupcf")]
    public void AppKey_FoldsVersionsCaseAndHyphens(string path, string expected)
    {
        Assert.Equal(expected, AddonManagerApps.AppKey(path));
    }

    [Fact]
    public void DetectLinux_ReadsEnabledAutostartEntriesAndRunningProcesses()
    {
        var root = Directory.CreateTempSubdirectory("steward-apps-").FullName;
        try
        {
            var autostart = Directory.CreateDirectory(Path.Combine(root, "autostart")).FullName;
            var proc = Directory.CreateDirectory(Path.Combine(root, "proc")).FullName;
            var wowUpCf = Path.Combine(root, "WowUp-CF-2.24.0.AppImage");
            var curseForge = Path.Combine(root, "curseforge");
            File.WriteAllText(wowUpCf, string.Empty);
            File.WriteAllText(curseForge, string.Empty);
            File.WriteAllText(Path.Combine(autostart, "WowUp.desktop"), $"[Desktop Entry]\nType=Application\nName=WowUp\nExec=\"{wowUpCf}\" --hidden\n");
            File.WriteAllText(Path.Combine(autostart, "curseforge.desktop"), $"[Desktop Entry]\nExec={curseForge}\nHidden=true\n");
            File.WriteAllText(Path.Combine(autostart, "missing.desktop"), $"[Desktop Entry]\nExec={Path.Combine(root, "WowUp-2.24.0.AppImage")}\n");
            Directory.CreateDirectory(Path.Combine(proc, "10"));
            File.WriteAllText(Path.Combine(proc, "10", "cmdline"), "/tmp/.mount_Curse/AppRun\0");
            File.WriteAllText(Path.Combine(proc, "10", "environ"), "APPIMAGE=/home/u/CurseForge.AppImage\0");
            Directory.CreateDirectory(Path.Combine(proc, "11"));
            File.WriteAllText(Path.Combine(proc, "11", "cmdline"), "/usr/bin/wowupdater\0");

            var apps = AddonManagerApps.DetectLinux(autostart, proc);

            Assert.Equal(
                [new AddonManagerApp("CurseForge", false, true), new AddonManagerApp("WowUp-CF", true, false)],
                apps);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DetectLinux_FindsNothingWithoutAutostartOrProcesses()
    {
        var root = Directory.CreateTempSubdirectory("steward-apps-").FullName;
        try
        {
            Assert.Empty(AddonManagerApps.DetectLinux(Path.Combine(root, "autostart"), Path.Combine(root, "proc")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("battlenet-1700000000.yml", "lutris:rungame/battlenet")]
    [InlineData("battlenet.yml", "lutris:rungame/battlenet")]
    [InlineData("battlenet-standard-1700000000.yml", null)]
    [InlineData("diablo-1700000000.yml", null)]
    public void LutrisLink_NeedsABattleNetGame(string config, string? expected)
    {
        Assert.Equal(expected, BattleNet.LutrisLink([Path.Combine("games", config)]));
    }
}
