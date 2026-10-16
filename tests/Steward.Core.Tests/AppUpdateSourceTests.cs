namespace Steward.Core.Tests;

public sealed class AppUpdateSourceTests
{
    [Theory]
    [InlineData(true, true, true, false, AppUpdateSource.Store)]
    [InlineData(true, false, true, false, AppUpdateSource.Msi)]
    [InlineData(true, true, false, false, AppUpdateSource.None)]
    [InlineData(true, false, false, false, AppUpdateSource.None)]
    [InlineData(false, false, true, false, AppUpdateSource.ReleasePage)]
    [InlineData(false, false, false, false, AppUpdateSource.None)]
    [InlineData(true, false, true, true, AppUpdateSource.ReleasePage)]
    [InlineData(true, false, false, true, AppUpdateSource.None)]
    public void Resolve_PicksTheSourceForTheOsAndBuild(bool isWindows, bool isPackaged, bool isGitHubRelease, bool isPortableZip, AppUpdateSource expected) =>
        Assert.Equal(expected, AppUpdateSources.Resolve(isWindows, isPackaged, isGitHubRelease, isPortableZip));

    [Theory]
    [InlineData("0.24.1-preview.62", "preview")]
    [InlineData("0.24.1", "portable")]
    [InlineData(null, "portable")]
    public void PortableZipBuildName_KeepsThePreviewTrainOffTheRelease(string? displayVersion, string expected) =>
        Assert.Equal(expected, AppUpdateSources.PortableZipBuildName(displayVersion));
}
