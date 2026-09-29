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
}
