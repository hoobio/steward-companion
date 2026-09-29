namespace Steward.Core.Tests;

public sealed class CurseForgeLinksTests
{
    private static readonly DateTimeOffset Released = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    private static AddonRelease Release(string version = "1.2.0", bool distributable = true) =>
        new(version, distributable ? "https://edge.forgecdn.net/a.zip" : null, null, 1, Released, Sha1: distributable ? "ab" : null, Distributable: distributable);

    private static CurseForgeModFile File(IReadOnlyList<int>? versionTypes = null, bool allow = true, int? releaseType = 1) =>
        new(1, "AtlasLoot Forever", null, "https://www.curseforge.com/wow/addons/atlasloot-forever", allow, Release(distributable: allow), releaseType, versionTypes ?? [88568]);

    [Theory]
    [InlineData("curseforge://install?addonId=3358&fileId=8996374", 3358, 8996374L)]
    [InlineData("CurseForge://INSTALL?fileId=9001874&addonId=12", 12, 9001874L)]
    public void Parse_ReadsBothIds(string uri, int modId, long fileId) =>
        Assert.Equal(new CurseForgeLink(modId, fileId), CurseForgeLinks.Parse(uri));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://install?addonId=1&fileId=2")]
    [InlineData("curseforge://open?addonId=1&fileId=2")]
    [InlineData("curseforge://install?addonId=0&fileId=2")]
    [InlineData("curseforge://install?addonId=-1&fileId=2")]
    [InlineData("curseforge://install?addonId=1&fileId=x")]
    [InlineData("curseforge://install?addonId=1")]
    [InlineData("curseforge://install?addonId=1.5&fileId=2")]
    [InlineData("curseforge://install?addonId=99999999999&fileId=2")]
    public void Parse_RejectsAnythingButTwoPositiveIntegers(string? uri) => Assert.Null(CurseForgeLinks.Parse(uri));

    [Theory]
    [InlineData(1, "release")]
    [InlineData(2, "pre-release")]
    [InlineData(3, "pre-release")]
    [InlineData(null, "release")]
    public void Channel_MapsReleaseType(int? releaseType, string channel) => Assert.Equal(channel, CurseForgeLinks.Channel(releaseType));

    [Fact]
    public void IsNewer_SameVersion_IsFalse() =>
        Assert.False(CurseForgeLinks.IsNewer(Release("1.2.0"), "1.2.0", null, Released.AddDays(-10)));

    [Fact]
    public void IsNewer_ComparesAgainstTheInstalledFilesReleaseDate_WhenKnown()
    {
        Assert.True(CurseForgeLinks.IsNewer(Release(), "1.1.0", Released.AddDays(-1), null));
        Assert.False(CurseForgeLinks.IsNewer(Release(), "1.3.0", Released.AddDays(1), Released.AddDays(-5)));
    }

    [Fact]
    public void IsNewer_FallsBackToTheInstallTime()
    {
        Assert.True(CurseForgeLinks.IsNewer(Release(), "1.1.0", null, Released.AddDays(-1)));
        Assert.False(CurseForgeLinks.IsNewer(Release(), "1.1.0", null, Released.AddDays(1)));
    }

    [Fact]
    public void IsNewer_WithNothingToCompare_IsFalse() => Assert.False(CurseForgeLinks.IsNewer(Release(), "unknown", null, null));

    [Fact]
    public void State_FileWithoutTheInstallsVersionType_IsNotBuilt()
    {
        Assert.Equal(CurseForgeLinkState.NotBuilt, CurseForgeLinks.State(File([517]), 88568, installed: false, newer: false));
        Assert.Equal(CurseForgeLinkState.NotBuilt, CurseForgeLinks.State(File(), null, installed: false, newer: false));
    }

    [Fact]
    public void State_NotDistributable_OffersCurseForge() =>
        Assert.Equal(CurseForgeLinkState.NotDistributable, CurseForgeLinks.State(File(allow: false), 88568, installed: false, newer: false));

    [Theory]
    [InlineData(false, false, CurseForgeLinkState.Install)]
    [InlineData(true, false, CurseForgeLinkState.Installed)]
    [InlineData(true, true, CurseForgeLinkState.Update)]
    public void State_InstallOrUpdate(bool installed, bool newer, CurseForgeLinkState expected) =>
        Assert.Equal(expected, CurseForgeLinks.State(File(), 88568, installed, newer));

    [Fact]
    public void State_InstalledAndNotNewer_IsInstalledEvenWhenNotDistributable() =>
        Assert.Equal(CurseForgeLinkState.Installed, CurseForgeLinks.State(File(allow: false), 88568, installed: true, newer: false));
}
