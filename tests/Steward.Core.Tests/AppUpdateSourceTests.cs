namespace Steward.Core.Tests;

public sealed class AppUpdateSourceTests
{
    [Theory]
    [InlineData(true, true, true, false, AppUpdateSource.Store)]
    [InlineData(true, false, true, false, AppUpdateSource.Msi)]
    [InlineData(true, true, false, false, AppUpdateSource.None)]
    [InlineData(true, false, false, false, AppUpdateSource.None)]
    [InlineData(true, false, true, true, AppUpdateSource.ReleasePage)]
    [InlineData(true, true, true, true, AppUpdateSource.ReleasePage)]
    [InlineData(true, false, false, true, AppUpdateSource.None)]
    [InlineData(false, false, true, false, AppUpdateSource.ReleasePage)]
    [InlineData(false, false, true, true, AppUpdateSource.ReleasePage)]
    [InlineData(false, false, false, true, AppUpdateSource.None)]
    public void Resolve_PicksTheSourceForTheOsAndBuild(bool isWindows, bool isPackaged, bool isGitHubRelease, bool isAvalonia, AppUpdateSource expected) =>
        Assert.Equal(expected, AppUpdateSources.Resolve(isWindows, isPackaged, isGitHubRelease, isAvalonia));
}
