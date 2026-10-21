namespace Steward.Core;

public static class ForeverLaunch
{
    public const string BetaProduct = "wow_classic_beta";
    public const string LiveProduct = "wowf";
    public static readonly DateOnly Date = new(2026, 10, 21);

    // Nobody plays the live client before launch, so any Steward saved variables there are copies of the beta's.
    public static bool HasNoCharacters(string? productCode, DateTimeOffset now) =>
        productCode == LiveProduct && now < ForeverSchedule.Launch.Start;

    public static WowInstall? MoveFromBeta(IEnumerable<WowInstall> installs, WowInstall? selected, DateOnly today, bool alreadyMoved) =>
        alreadyMoved || today < Date || selected?.ProductCode is not BetaProduct
            ? null
            : installs.FirstOrDefault(install => install.ProductCode == LiveProduct && !install.IsMissing);
}
