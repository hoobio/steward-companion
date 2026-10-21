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
