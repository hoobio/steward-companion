namespace Steward.Core.Tests;

public sealed class InstallNamesTests
{
    private const string Beta = "Forever - Beta";

    [Fact]
    public void Resolve_NumbersTheSecondOfTwoDefaults() =>
        Assert.Equal([Beta, $"{Beta} (2)"], InstallNames.Resolve([(null, Beta), (null, Beta)]));

    [Fact]
    public void Resolve_NumbersThreeDefaultsInListOrder() =>
        Assert.Equal([Beta, $"{Beta} (2)", $"{Beta} (3)"], InstallNames.Resolve([(null, Beta), (null, Beta), (null, Beta)]));

    [Fact]
    public void Resolve_SkipsALabelledInstallInBetween() =>
        Assert.Equal([Beta, "UI Test", $"{Beta} (2)"], InstallNames.Resolve([(null, Beta), ("UI Test", Beta), (null, Beta)]));

    [Fact]
    public void Resolve_KeepsALabelEqualToADefaultNameVerbatim() =>
        Assert.Equal([Beta, Beta], InstallNames.Resolve([(Beta, Beta), (null, Beta)]));

    [Fact]
    public void Resolve_LeavesDistinctDefaultsPlain() =>
        Assert.Equal([Beta, "Forever"], InstallNames.Resolve([(null, Beta), (null, "Forever")]));
}
