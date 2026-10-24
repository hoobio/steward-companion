namespace Steward.Core.Tests;

public sealed class ForeverLaunchTests
{
    private static readonly WowInstall Beta = Install("_classic_beta_", ForeverLaunch.BetaProduct);
    private static readonly WowInstall Live = Install("_forever_", ForeverLaunch.LiveProduct);
    private static readonly DateOnly Launch = ForeverLaunch.Date;

    private static WowInstall Install(string flavour, string product, bool missing = false) =>
        new(@"C:\wow", flavour, $@"C:\wow\{flavour}",$@"C:\wow\{flavour}\Interface\AddOns", product, "1.60.1.70245", flavour, missing);

    [Fact]
    public void MovesBetaSelectionToLiveOnLaunchDay() =>
        Assert.Equal(Live, ForeverLaunch.MoveFromBeta([Beta, Live], Beta, Launch, alreadyMoved: false));

    [Fact]
    public void KeepsBetaBeforeLaunch() =>
        Assert.Null(ForeverLaunch.MoveFromBeta([Beta, Live], Beta, Launch.AddDays(-1), alreadyMoved: false));

    [Fact]
    public void TheLiveClientHasNoCharactersUntilLaunch()
    {
        var launch = ForeverSchedule.Launch.Start;
        Assert.True(ForeverLaunch.HasNoCharacters(ForeverLaunch.LiveProduct, launch.AddMinutes(-1)));
        Assert.False(ForeverLaunch.HasNoCharacters(ForeverLaunch.LiveProduct, launch));
        Assert.False(ForeverLaunch.HasNoCharacters(ForeverLaunch.BetaProduct, launch.AddDays(-1)));
    }

    [Theory]
    [InlineData("wowf", true)]
    [InlineData("wow_classic_beta", true)]
    [InlineData("wow", false)]
    [InlineData("wow_classic_era", false)]
    [InlineData(null, false)]
    public void IsForever_OnlyForTheLiveAndBetaClients(string? product, bool expected) =>
        Assert.Equal(expected, ForeverLaunch.IsForever(product));

    [Fact]
    public void ReadsCharacters_OnlyOnForeverAndNotOnLiveBeforeLaunch()
    {
        var launch = ForeverSchedule.Launch.Start;
        Assert.True(ForeverLaunch.ReadsCharacters(ForeverLaunch.BetaProduct, launch.AddDays(-1)));
        Assert.False(ForeverLaunch.ReadsCharacters(ForeverLaunch.LiveProduct, launch.AddMinutes(-1)));
        Assert.True(ForeverLaunch.ReadsCharacters(ForeverLaunch.LiveProduct, launch));
        Assert.False(ForeverLaunch.ReadsCharacters("wow_classic_era", launch));
        Assert.False(ForeverLaunch.ReadsCharacters(null, launch));
    }

    [Fact]
    public void MovesOnlyOnce() =>
        Assert.Null(ForeverLaunch.MoveFromBeta([Beta, Live], Beta, Launch, alreadyMoved: true));

    [Fact]
    public void LeavesOtherSelectionsAlone() =>
        Assert.Null(ForeverLaunch.MoveFromBeta([Beta, Live, Install("_retail_", "wow")], Install("_retail_", "wow"), Launch, alreadyMoved: false));

    [Fact]
    public void WaitsForAPresentLiveInstall()
    {
        Assert.Null(ForeverLaunch.MoveFromBeta([Beta], Beta, Launch, alreadyMoved: false));
        Assert.Null(ForeverLaunch.MoveFromBeta([Beta, Install("_forever_", ForeverLaunch.LiveProduct, missing: true)], Beta, Launch, alreadyMoved: false));
    }
}
