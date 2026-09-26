namespace Steward.Core.Tests;

public sealed class AppUpdaterTests
{
    [Fact]
    public void SwitchToStoreScript_UninstallsRelatedProductsBeforeLaunchingTheStoreApp()
    {
        var script = AppUpdater.SwitchToStoreScript("{CCD0BF88-7A8E-4F74-9DB7-9B9272B3D503}", @"C:\Logs\update.log", "Hoobi.Steward_thayxpy3eqg0g!App");

        var uninstall = script.IndexOf("RelatedProducts('{CCD0BF88-7A8E-4F74-9DB7-9B9272B3D503}')", StringComparison.Ordinal);
        var removeRun = script.IndexOf("Remove-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -Name 'Steward'", StringComparison.Ordinal);
        var launch = script.IndexOf(@"Start-Process explorer.exe -ArgumentList 'shell:AppsFolder\Hoobi.Steward_thayxpy3eqg0g!App'", StringComparison.Ordinal);
        Assert.True(uninstall >= 0 && uninstall < removeRun && removeRun < launch, script);
        Assert.Contains(@"'/x', $productCode, '/qn', '/l*v', '""C:\Logs\update.log""' -PassThru -Wait", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchToStoreScript_StopsBeforeRemovingRunValueOnAFailedUninstall()
    {
        var script = AppUpdater.SwitchToStoreScript("{CCD0BF88-7A8E-4F74-9DB7-9B9272B3D503}", @"C:\Logs\update.log", "Hoobi.Steward_thayxpy3eqg0g!App");

        var guard = script.IndexOf("if ($exitCode -ne 0 -and $exitCode -ne 3010) { exit $exitCode }", StringComparison.Ordinal);
        var removeRun = script.IndexOf("Remove-ItemProperty", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < removeRun, script);
    }

    [Fact]
    public void SwitchToStoreScript_EscapesASingleQuoteInTheLogPath()
    {
        var script = AppUpdater.SwitchToStoreScript("{CCD0BF88-7A8E-4F74-9DB7-9B9272B3D503}", @"C:\O'Brien\update.log", "Hoobi.Steward_thayxpy3eqg0g!App");

        Assert.Contains(@"'""C:\O''Brien\update.log""'", script, StringComparison.Ordinal);
    }
}
