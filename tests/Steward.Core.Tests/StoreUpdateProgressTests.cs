namespace Steward.Core.Tests;

public sealed class StoreUpdateProgressTests
{
    private const ulong Total = 30_513_561;

    [Fact]
    public void From_Downloading_ScalesTheDownloadShareToAHundredPercent()
    {
        var progress = StoreUpdateProgress.From(AppUpdatePhase.Downloading, 0.4, 12_897_484, Total);

        Assert.Equal(AppUpdatePhase.Downloading, progress.Phase);
        Assert.Equal(50, progress.Percent);
        Assert.Equal("Downloading update… 50% (12.3 of 29.1 MB)", progress.Text);
        Assert.False(progress.IsIndeterminate);
    }

    [Fact]
    public void From_DownloadingPastTheDownloadShare_IsInstalling()
    {
        var progress = StoreUpdateProgress.From(AppUpdatePhase.Downloading, 0.85, Total, Total);

        Assert.Equal(AppUpdatePhase.Installing, progress.Phase);
        Assert.Equal("Installing update… Steward will restart", progress.Text);
        Assert.True(progress.IsIndeterminate);
    }

    [Fact]
    public void From_DownloadingWithUnknownSize_OmitsTheMegabytes()
    {
        Assert.Equal("Downloading update… 0%", StoreUpdateProgress.From(AppUpdatePhase.Downloading, 0, 0, 0).Text);
    }

    [Theory]
    [InlineData(AppUpdatePhase.Pending, true, "Waiting for the Microsoft Store…")]
    [InlineData(AppUpdatePhase.None, false, "")]
    [InlineData(AppUpdatePhase.Failed, false, "")]
    public void From_OtherPhases_MapToTheirText(AppUpdatePhase phase, bool active, string text)
    {
        var progress = StoreUpdateProgress.From(phase, 0.5, 1, 2);

        Assert.Equal(active, progress.IsActive);
        Assert.Equal(text, progress.Text);
    }

    [Theory]
    [InlineData(0UL, "0.0")]
    [InlineData(1_048_576UL, "1.0")]
    [InlineData(12_897_484UL, "12.3")]
    public void Megabytes_FormatsOneDecimal(ulong bytes, string expected) =>
        Assert.Equal(expected, StoreUpdateProgress.Megabytes(bytes));
}
