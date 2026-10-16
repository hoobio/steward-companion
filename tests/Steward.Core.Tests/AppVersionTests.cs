namespace Steward.Core.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("0.23.2-preview.23", "0.23.2-preview.9", true)]
    [InlineData("0.23.2-preview.9", "0.23.2-preview.23", false)]
    [InlineData("0.23.2-preview.31", "0.23.2-preview.31", false)]
    [InlineData("0.23.2", "0.23.2-preview.31", true)]
    [InlineData("0.23.2-preview.31", "0.23.2", false)]
    [InlineData("0.23.3-preview.1", "0.23.2", true)]
    [InlineData("v0.24.0", "0.23.2-preview.99", true)]
    [InlineData("0.23.2-preview.10", "0.23.2-preview.1", true)]
    [InlineData("0.23.2-rc.1", "0.23.2-preview.40", true)]
    [InlineData("0.23.2-preview.1.1", "0.23.2-preview.1", true)]
    [InlineData("0.23.2-preview.a", "0.23.2-preview.9", true)]
    [InlineData("0.20.2", "0.20.2.0", false)]
    [InlineData("0.20.2.1", "0.20.2", true)]
    [InlineData("0.23.2+build.5", "0.23.2", false)]
    public void IsNewer_UsesSemverPrecedence(string available, string installed, bool expected) =>
        Assert.Equal(expected, AppVersion.IsNewer(available, installed));

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-preview..1")]
    public void Compare_UnparseableSide_IsNull(string version) =>
        Assert.Null(AppVersion.Compare(version, "0.23.2"));

    [Fact]
    public void FromPackageVersion_MapsTheTildeBack() =>
        Assert.Equal("0.23.2-preview.31", AppVersion.FromPackageVersion("0.23.2~preview.31\n"));

    [Theory]
    [InlineData("0.24.1~preview.3", "0.24.0", true)]
    [InlineData("0.24.1", "0.24.1~preview.3", true)]
    [InlineData("0.24.2~preview.1", "0.24.1", true)]
    [InlineData("0.24.0~preview.90", "0.24.0", false)]
    [InlineData("0.24.1~preview.4", "0.24.1~preview.3", true)]
    public void IsNewer_PackageVersionsKeepThePreviewsBetweenReleases(string available, string installed, bool expected) =>
        Assert.Equal(expected, AppVersion.IsNewer(AppVersion.FromPackageVersion(available), AppVersion.FromPackageVersion(installed)));

    [Theory]
    [InlineData("0.23.2-preview.31", true)]
    [InlineData("v0.23.2", false)]
    [InlineData("0.23.2+meta", false)]
    public void IsPreRelease_ReadsTheSuffix(string version, bool expected) =>
        Assert.Equal(expected, AppVersion.IsPreRelease(version));
}
