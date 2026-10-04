namespace Steward.Core.Tests;

public sealed class ColumnFitTests
{
    [Fact]
    public void TypicalMax_NoWidths_ReturnsZero() => Assert.Equal(0, ColumnFit.TypicalMax([]));

    [Fact]
    public void TypicalMax_OneOutlier_ReturnsTheLongestOtherWidth() =>
        Assert.Equal(160, ColumnFit.TypicalMax([50, 160, 70, 100, 50, 72, 120, 330, 75]));

    [Fact]
    public void TypicalMax_NoOutlier_ReturnsTheMax() => Assert.Equal(140, ColumnFit.TypicalMax([90, 100, 110, 120, 140]));

    [Fact]
    public void TypicalMax_OneWidth_ReturnsIt() => Assert.Equal(330, ColumnFit.TypicalMax([330]));
}
