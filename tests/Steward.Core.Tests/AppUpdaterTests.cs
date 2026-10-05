namespace Steward.Core.Tests;

public sealed class AppUpdaterTests
{
    [Theory]
    [InlineData("v0.20.2", "0.20.1", true)]
    [InlineData("0.21.0", "0.20.9", true)]
    [InlineData("v1.0.0", "0.99.99", true)]
    [InlineData("v0.20.2", "0.20.2", false)]
    [InlineData("0.20.2", "0.20.2.0", false)]
    [InlineData("v0.20.1", "0.20.2", false)]
    [InlineData("v0.10.0", "0.9.0", true)]
    [InlineData("V0.20.3", "0.20.2", true)]
    [InlineData("not-a-version", "0.20.2", false)]
    [InlineData("v0.20.3", "unknown", false)]
    [InlineData("", "0.20.2", false)]
    public void IsNewer_ComparesNumerically(string available, string installed, bool expected) =>
        Assert.Equal(expected, AppUpdater.IsNewer(available, installed));

    [Fact]
    public void InstallMsiScript_InstallsQuietlyWithAVerboseLog()
    {
        var script = AppUpdater.InstallMsiScript(@"C:\Temp\Steward-0.20.2.msi", @"C:\Logs\update.log");

        Assert.Contains(@"Start-Process msiexec.exe -ArgumentList '/i', '""C:\Temp\Steward-0.20.2.msi""', '/qn', '/l*v', '""C:\Logs\update.log""' -PassThru -Wait", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallMsiScript_StopsBeforeDeletingTheMsiOnAFailedInstall()
    {
        var script = AppUpdater.InstallMsiScript(@"C:\Temp\Steward-0.20.2.msi", @"C:\Logs\update.log");

        var install = script.IndexOf("msiexec.exe", StringComparison.Ordinal);
        var guard = script.IndexOf("if ($exitCode -ne 0 -and $exitCode -ne 3010) { exit $exitCode }", StringComparison.Ordinal);
        var delete = script.IndexOf(@"Remove-Item -LiteralPath 'C:\Temp\Steward-0.20.2.msi'", StringComparison.Ordinal);
        Assert.True(install >= 0 && install < guard && guard < delete, script);
    }

    [Fact]
    public void InstallMsiScript_EscapesSingleQuotes()
    {
        var script = AppUpdater.InstallMsiScript(@"C:\O'Brien\Steward-0.20.2.msi", @"C:\O'Brien\update.log");

        Assert.Contains(@"'""C:\O''Brien\Steward-0.20.2.msi""'", script, StringComparison.Ordinal);
        Assert.Contains(@"'""C:\O''Brien\update.log""'", script, StringComparison.Ordinal);
        Assert.Contains(@"Remove-Item -LiteralPath 'C:\O''Brien\Steward-0.20.2.msi'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallMsiScript_LeavesTheRelaunchToTheMsi()
    {
        var script = AppUpdater.InstallMsiScript(@"C:\Temp\Steward-0.20.2.msi", @"C:\Logs\update.log");

        Assert.DoesNotContain("Steward.App.exe", script, StringComparison.Ordinal);
    }
}
