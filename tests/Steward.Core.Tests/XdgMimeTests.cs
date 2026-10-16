namespace Steward.Core.Tests;

public sealed class XdgMimeTests
{
    private sealed class FakeRunner(int exitCode, string output)
    {
        public List<(string File, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public (int, string) Run(string file, IReadOnlyList<string> arguments)
        {
            Calls.Add((file, arguments));
            return (exitCode, output);
        }
    }

    [Theory]
    [InlineData(0, "steward.desktop\n", true)]
    [InlineData(0, "curseforge.desktop\n", false)]
    [InlineData(0, "", false)]
    [InlineData(1, "steward.desktop\n", false)]
    public void IsDefault_QueriesTheCurseForgeScheme(int exitCode, string output, bool expected)
    {
        var runner = new FakeRunner(exitCode, output);

        Assert.Equal(expected, XdgMime.IsDefault(runner.Run));
        var call = Assert.Single(runner.Calls);
        Assert.Equal("xdg-mime", call.File);
        Assert.Equal(["query", "default", "x-scheme-handler/curseforge"], call.Arguments);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    public void SetDefault_PointsTheSchemeAtStewardsDesktopFile(int exitCode, bool expected)
    {
        var runner = new FakeRunner(exitCode, "");

        Assert.Equal(expected, XdgMime.SetDefault(runner.Run));
        var call = Assert.Single(runner.Calls);
        Assert.Equal("xdg-mime", call.File);
        Assert.Equal(["default", "steward.desktop", "x-scheme-handler/curseforge"], call.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    public void ApplicationDirs_DefaultsFollowTheXdgSpec(string? dataHome) =>
        Assert.Equal(
            [Path.Combine("home", ".local", "share", "applications"), Path.Combine("/usr/local/share", "applications"), Path.Combine("/usr/share", "applications")],
            XdgMime.ApplicationDirs(dataHome, null, "home"));

    [Fact]
    public void ApplicationDirs_ReadTheEnvironmentValues() =>
        Assert.Equal(
            [Path.Combine("/data", "applications"), Path.Combine("/a", "applications"), Path.Combine("/b", "applications")],
            XdgMime.ApplicationDirs("/data", "/a::/b", "home"));
}
