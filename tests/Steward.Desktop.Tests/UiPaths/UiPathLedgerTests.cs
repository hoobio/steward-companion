namespace Steward.Desktop.Tests.UiPaths;

public sealed class UiPathLedgerTests
{
    private static readonly string[] Extras = ["152", "155", "156", "157", "211", "212", "230", "231", "232", "241", "260", "X1", "X2", "X3", "X4"];

    [Fact]
    public void Every_ledger_path_is_driven_or_skipped_with_a_reason()
    {
        var expected = Enumerable.Range(0, 148).Select(UiPathLedger.Id).Concat(Extras).ToList();

        Assert.Equal(expected, UiPathLedger.Paths.Select(path => path.Id));
        Assert.All(UiPathLedger.Paths, path => Assert.True(path.Skip is null or { Length: > 0 }, path.Id));
        foreach (var (id, _, skip) in UiPathLedger.Paths.Where(path => path.Skip is not null))
        {
            UiPathLedger.Record(id, $"skip: {skip}");
        }
    }
}
