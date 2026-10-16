using System.Runtime.InteropServices;

namespace Steward.Core.Tests;

public sealed class LinuxPackagesTests : IDisposable
{
    private const string Exe = "/opt/steward/Steward.Desktop";

    private readonly DirectoryInfo _etc = Directory.CreateTempSubdirectory("steward-etc-");

    public void Dispose() => _etc.Delete(recursive: true);

    private void WriteEtc(string relative, string content)
    {
        var path = Path.Combine(_etc.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static Func<string, IReadOnlyList<string>, CommandResult?> Owner(string? dpkg, string? rpm) => (program, args) => program switch
    {
        "dpkg-query" when dpkg is not null && args.SequenceEqual(["-S", Exe]) => new CommandResult(0, dpkg),
        "dpkg-query" when dpkg is not null => new CommandResult(1, ""),
        "rpm" when rpm is not null && args.SequenceEqual(["-qf", "--queryformat", "%{NAME}", Exe]) => new CommandResult(0, rpm),
        "rpm" when rpm is not null => new CommandResult(1, ""),
        _ => null,
    };

    [Fact]
    public void Detect_DebFromTheFeed_FindsTheSourcesFile()
    {
        WriteEtc("apt/sources.list.d/ubuntu.sources", "URIs: http://archive.ubuntu.com/ubuntu\n");
        WriteEtc("apt/sources.list.d/steward.list", "deb [signed-by=/etc/apt/keyrings/steward.gpg] https://hoobio.github.io/steward-companion/apt preview main\n");

        var install = LinuxPackages.Detect(Exe, Owner("steward: /opt/steward/Steward.Desktop\n", null), _etc.FullName);

        Assert.Equal(LinuxPackageKind.Deb, install.Kind);
        Assert.Equal(Path.Combine(_etc.FullName, "apt", "sources.list.d", "steward.list"), install.FeedSource);
    }

    [Fact]
    public void Detect_Deb822FeedFile_CountsAsTheFeed()
    {
        WriteEtc("apt/sources.list.d/hoobi.sources", "Types: deb\nURIs: https://hoobio.github.io/steward-companion/apt\nSuites: preview\n");

        Assert.True(LinuxPackages.Detect(Exe, Owner("steward: /opt/steward/Steward.Desktop", null), _etc.FullName).FromFeed);
    }

    [Fact]
    public void Detect_DebWithTheFeedCommentedOut_IsAPlainPackage()
    {
        WriteEtc("apt/sources.list.d/steward.list", "# deb https://hoobio.github.io/steward-companion/apt preview main\n");

        var install = LinuxPackages.Detect(Exe, Owner("steward: /opt/steward/Steward.Desktop", null), _etc.FullName);

        Assert.Equal(new LinuxInstall(LinuxPackageKind.Deb, null), install);
    }

    [Fact]
    public void Detect_RpmFromTheFeed_FindsTheRepoFile()
    {
        WriteEtc("yum.repos.d/steward.repo", "[steward-preview]\nbaseurl=https://hoobio.github.io/steward-companion/rpm/preview/$basearch\n");

        var install = LinuxPackages.Detect(Exe, Owner(null, "steward"), _etc.FullName);

        Assert.Equal(LinuxPackageKind.Rpm, install.Kind);
        Assert.True(install.FromFeed);
    }

    [Fact]
    public void Detect_StableFeed_IsNotThePreviewChannel()
    {
        WriteEtc("apt/sources.list.d/steward.list", "deb [signed-by=/etc/apt/keyrings/steward.gpg] https://hoobio.github.io/steward-companion/apt stable main\n");

        var install = LinuxPackages.Detect(Exe, Owner("steward: /opt/steward/Steward.Desktop", null), _etc.FullName);

        Assert.True(install.FromFeed);
        Assert.False(install.PreviewFeed);
    }

    [Fact]
    public void Detect_StableAndPreviewRepos_PicksThePreviewOne()
    {
        WriteEtc("yum.repos.d/steward.repo", "[steward]\nbaseurl=https://hoobio.github.io/steward-companion/rpm/stable/$basearch\n");
        WriteEtc("yum.repos.d/steward-preview.repo", "[steward-preview]\nbaseurl=https://hoobio.github.io/steward-companion/rpm/preview/$basearch\n");

        var install = LinuxPackages.Detect(Exe, Owner(null, "steward"), _etc.FullName);

        Assert.Equal(Path.Combine(_etc.FullName, "yum.repos.d", "steward-preview.repo"), install.FeedSource);
        Assert.True(install.PreviewFeed);
    }

    [Theory]
    [InlineData("deb https://hoobio.github.io/steward-companion/apt preview main", true)]
    [InlineData("Types: deb\nURIs: https://hoobio.github.io/steward-companion/apt\nSuites: preview", true)]
    [InlineData("deb https://hoobio.github.io/steward-companion/apt stable main\n# deb https://hoobio.github.io/steward-companion/apt preview main", false)]
    public void Detect_ReadsTheSuiteFromTheFeedFile(string content, bool preview)
    {
        WriteEtc("apt/sources.list.d/steward.sources", content + "\n");

        Assert.Equal(preview, LinuxPackages.Detect(Exe, Owner("steward: /opt/steward/Steward.Desktop", null), _etc.FullName).PreviewFeed);
    }

    [Theory]
    [InlineData("0.24.0", "/etc/apt/sources.list.d/steward.list", true, true)]
    [InlineData("0.24.0", "/etc/apt/sources.list.d/steward.list", false, false)]
    [InlineData("0.24.0", null, false, false)]
    [InlineData("0.24.1-preview.62", null, false, true)]
    [InlineData("0.24.1-preview.62", "/etc/apt/sources.list.d/steward.list", false, false)]
    public void IncludesPreReleases_TheFeedDecidesElseTheRunningBuild(string running, string? feed, bool previewFeed, bool expected) =>
        Assert.Equal(expected, new LinuxInstall(LinuxPackageKind.Deb, feed, previewFeed).IncludesPreReleases(running));

    [Theory]
    [InlineData(null, null)]
    [InlineData("other: /opt/steward/Steward.Desktop", null)]
    [InlineData(null, "other")]
    public void Detect_NotOwnedBySteward_IsNone(string? dpkg, string? rpm) =>
        Assert.Equal(LinuxPackageKind.None, LinuxPackages.Detect(Exe, Owner(dpkg, rpm), _etc.FullName).Kind);

    [Theory]
    [InlineData(LinuxPackageKind.Deb, Architecture.X64, "-linux-x64.deb")]
    [InlineData(LinuxPackageKind.Deb, Architecture.Arm64, "-linux-arm64.deb")]
    [InlineData(LinuxPackageKind.Rpm, Architecture.X64, "-linux-x64.rpm")]
    [InlineData(LinuxPackageKind.None, Architecture.X64, "-linux-x64.deb")]
    [InlineData(LinuxPackageKind.Deb, Architecture.Arm, null)]
    public void AssetSuffix_FollowsFormatAndArchitecture(LinuxPackageKind kind, Architecture architecture, string? expected) =>
        Assert.Equal(expected, LinuxPackages.AssetSuffix(kind, architecture));

    [Fact]
    public void InstallArguments_AptFeed_IsOnePkexecWithTheSourceAsAnArgument()
    {
        var argv = LinuxPackages.InstallArguments(new LinuxInstall(LinuxPackageKind.Deb, "/etc/apt/sources.list.d/my 'odd'.list"), null, elevated: false);

        Assert.Equal(["pkexec", "/bin/sh", "-c", LinuxPackages.AptFeedScript, "steward-update", "/etc/apt/sources.list.d/my 'odd'.list"], argv);
        Assert.DoesNotContain("odd", LinuxPackages.AptFeedScript, StringComparison.Ordinal);
        Assert.Contains("apt-get update -o Dir::Etc::sourcelist=\"$1\" -o Dir::Etc::sourceparts=-", LinuxPackages.AptFeedScript, StringComparison.Ordinal);
        Assert.EndsWith("apt-get install -y --only-upgrade steward", LinuxPackages.AptFeedScript, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallArguments_EveryPath_IsAnArgvList()
    {
        Assert.Equal(["pkexec", "/usr/bin/dnf", "upgrade", "-y", "--refresh", "steward"],
            LinuxPackages.InstallArguments(new LinuxInstall(LinuxPackageKind.Rpm, "/etc/yum.repos.d/steward.repo"), null, elevated: false));
        Assert.Equal(["pkexec", "/usr/bin/apt-get", "install", "-y", "/home/u/.cache/x; rm -rf ~/Steward.deb"],
            LinuxPackages.InstallArguments(new LinuxInstall(LinuxPackageKind.Deb, null), "/home/u/.cache/x; rm -rf ~/Steward.deb", elevated: false));
        Assert.Equal(["/usr/bin/dnf", "install", "-y", "/tmp/s/Steward.rpm"],
            LinuxPackages.InstallArguments(new LinuxInstall(LinuxPackageKind.Rpm, null), "/tmp/s/Steward.rpm", elevated: true));
    }

    [Theory]
    [InlineData(LinuxPackageKind.Deb, "Steward.deb")]
    [InlineData(LinuxPackageKind.None, "/tmp/Steward.deb")]
    [InlineData(LinuxPackageKind.Rpm, null)]
    public void InstallArguments_NoPackageOrRelativePath_Throws(LinuxPackageKind kind, string? path) =>
        Assert.Throws<InvalidOperationException>(() => LinuxPackages.InstallArguments(new LinuxInstall(kind, null), path, elevated: false));

    [Theory]
    [InlineData(0, "", false, PackageInstallOutcome.Installed)]
    [InlineData(126, "", false, PackageInstallOutcome.Dismissed)]
    [InlineData(127, "Error executing command as another user: No authentication agent found.", false, PackageInstallOutcome.NoAuthenticationAgent)]
    [InlineData(127, "Error executing command as another user: Not authorized", false, PackageInstallOutcome.NotAuthorized)]
    [InlineData(127, "Cannot run program apt-get: No such file or directory", false, PackageInstallOutcome.Failed)]
    [InlineData(127, "/bin/sh: 1: dnf: not found", false, PackageInstallOutcome.Failed)]
    [InlineData(100, "E: Unable to locate package steward", false, PackageInstallOutcome.Failed)]
    [InlineData(126, "", true, PackageInstallOutcome.Failed)]
    public void Classify_ReadsPkexecExitCodes(int exitCode, string output, bool elevated, PackageInstallOutcome expected) =>
        Assert.Equal(expected, LinuxPackages.Classify(new CommandResult(exitCode, output), elevated));

    [Fact]
    public void TerminalCommand_NamesTheCommandForEachInstall()
    {
        Assert.Equal("sudo apt update && sudo apt install --only-upgrade steward", LinuxPackages.TerminalCommand(new LinuxInstall(LinuxPackageKind.Deb, "/etc/apt/sources.list.d/steward.list")));
        Assert.Equal("sudo dnf upgrade --refresh steward", LinuxPackages.TerminalCommand(new LinuxInstall(LinuxPackageKind.Rpm, "/etc/yum.repos.d/steward.repo")));
        Assert.Equal("sudo apt install ./Steward-0.23.2-preview.31-linux-x64.deb", LinuxPackages.TerminalCommand(new LinuxInstall(LinuxPackageKind.Deb, null), "Steward-0.23.2-preview.31-linux-x64.deb"));
        Assert.Null(LinuxPackages.TerminalCommand(new LinuxInstall(LinuxPackageKind.None, null)));
    }

    [Fact]
    public void RelaunchArguments_WaitForThisProcessThroughArgv()
    {
        Assert.Equal(["setsid", "-f", "/bin/sh", "-c", LinuxPackages.RelaunchScript, "steward-relaunch", "4242", "/opt/steward/Steward.Desktop"],
            LinuxPackages.RelaunchArguments(4242, "/opt/steward/Steward.Desktop", hasSetsid: true));
        Assert.Equal("/bin/sh", LinuxPackages.RelaunchArguments(4242, "/opt/steward/Steward.Desktop", hasSetsid: false)[0]);
        Assert.DoesNotContain("4242", LinuxPackages.RelaunchScript, StringComparison.Ordinal);
        Assert.EndsWith("exec \"$2\"", LinuxPackages.RelaunchScript, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledVersionArguments_QueryTheInstalledPackage()
    {
        Assert.Equal(["dpkg-query", "-W", "-f=${Version}", "steward"], LinuxPackages.InstalledVersionArguments(LinuxPackageKind.Deb));
        Assert.Equal(["rpm", "-q", "--queryformat", "%{VERSION}", "steward"], LinuxPackages.InstalledVersionArguments(LinuxPackageKind.Rpm));
        Assert.Null(LinuxPackages.InstalledVersionArguments(LinuxPackageKind.None));
    }
}
