namespace Steward.Core.Tests;

public sealed class AppUpdateCheckTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-update-check-").FullName;
    private string StatePath => Path.Combine(_root, "state.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Save_RoundTripsTheLastAppUpdateCheck()
    {
        var store = new AppStateStore([], StatePath);
        var check = new AppUpdateCheck("0.14.0", UpdateAvailable: false, new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

        store.Save(store.Load() with { AppUpdateCheck = check });

        Assert.Equal(check, store.Load().AppUpdateCheck);
    }

    [Fact]
    public void Load_LeavesTheCheckNull_ForAnOlderStateFile()
    {
        File.WriteAllText(StatePath, """{"channels":{},"installs":{}}""");

        Assert.Null(new AppStateStore([], StatePath).Load().AppUpdateCheck);
    }
}
