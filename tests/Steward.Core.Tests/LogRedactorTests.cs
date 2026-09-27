using Steward.Core.Diagnostics;

namespace Steward.Core.Tests;

public sealed class LogRedactorTests
{
    [Fact]
    public void PathOnly_DropsQueryAndFragment() =>
        Assert.Equal(
            "https://api.hoobi.io/guild/api/auth/desktop",
            LogRedactor.PathOnly("https://api.hoobi.io/guild/api/auth/desktop?challenge=deadbeef&port=54321"));

    [Fact]
    public void PathOnly_ReturnsTheInputUnchanged_WhenNotAnAbsoluteUri() =>
        Assert.Equal("not a url", LogRedactor.PathOnly("not a url"));
}
