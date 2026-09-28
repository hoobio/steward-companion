namespace Steward.Core.Tests;

public sealed class LocalAddonsTests : IDisposable
{
    private readonly string _addOnsPath = Directory.CreateTempSubdirectory("steward-local-addons-").FullName;

    public void Dispose() => Directory.Delete(_addOnsPath, recursive: true);

    private void WriteAddon(string folderName, string toc, string? tocFileName = null)
    {
        var folderPath = Path.Combine(_addOnsPath, folderName);
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, tocFileName ?? $"{folderName}.toc"), toc);
    }

    [Fact]
    public void Scan_MissingAddOnsFolder_ReturnsEmpty()
    {
        Assert.Empty(LocalAddons.Scan(Path.Combine(_addOnsPath, "missing"), []));
    }

    [Fact]
    public void Scan_ExcludesConfiguredFolders()
    {
        WriteAddon("Steward", "## Title: Steward\n");

        Assert.Empty(LocalAddons.Scan(_addOnsPath, ["Steward"]));
    }

    [Fact]
    public void Scan_ExcludesStewardGuides()
    {
        WriteAddon("StewardGuides", "## Title: StewardGuides\n");

        Assert.Empty(LocalAddons.Scan(_addOnsPath, ["StewardGuides"]));
    }

    [Fact]
    public void Scan_FolderWithNoToc_IsIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_addOnsPath, "NoToc"));

        Assert.Empty(LocalAddons.Scan(_addOnsPath, []));
    }

    [Fact]
    public void Scan_SuffixOnlyTocFolder_IsCounted()
    {
        WriteAddon("Grid2", "## Title: Grid2\n", "Grid2_Bar.toc");

        var addon = Assert.Single(LocalAddons.Scan(_addOnsPath, []));
        Assert.Equal("Grid2", addon.FolderName);
        Assert.Equal("Grid2", addon.Name);
    }

    [Fact]
    public void Scan_FolderDependingOnAnExcludedFolder_KeepsItsOwnRow()
    {
        WriteAddon("Steward", "## Title: Steward\n");
        WriteAddon("HoobiVersions", "## Title: HoobiVersions\n## Dependencies: Steward\n");

        var addons = LocalAddons.Scan(_addOnsPath, ["Steward"]);

        var addon = Assert.Single(addons);
        Assert.Equal("HoobiVersions", addon.FolderName);
        Assert.Empty(addon.FoldedFolders);
    }

    [Fact]
    public void Scan_DbmStyleChain_FoldsIntoTheRoot()
    {
        WriteAddon("DBM-Core", "## Title: Deadly Boss Mods\n");
        WriteAddon("DBM-DefaultSkin", "## Title: DBM-DefaultSkin\n## Dependencies: DBM-Core\n");
        WriteAddon("DBM-StatusBarTimers", "## Title: DBM-StatusBarTimers\n## RequiredDeps: DBM-DefaultSkin\n");

        var addons = LocalAddons.Scan(_addOnsPath, []);

        var addon = Assert.Single(addons);
        Assert.Equal("DBM-Core", addon.FolderName);
        Assert.Equal(["DBM-DefaultSkin", "DBM-StatusBarTimers"], addon.FoldedFolders);
    }

    [Fact]
    public void Scan_TwoCycle_KeepsBothRows()
    {
        WriteAddon("A", "## Title: A\n## Dependencies: B\n");
        WriteAddon("B", "## Title: B\n## Dependencies: A\n");

        var addons = LocalAddons.Scan(_addOnsPath, []);

        Assert.Equal(["A", "B"], addons.Select(a => a.FolderName).OrderBy(f => f, StringComparer.Ordinal));
        Assert.All(addons, a => Assert.Empty(a.FoldedFolders));
    }

    [Fact]
    public void Scan_LockedToc_SkipsThatFolderOnly()
    {
        WriteAddon("Locked", "## Title: Locked\n");
        WriteAddon("Open", "## Title: Open\n");

        using var lockHandle = File.Open(Path.Combine(_addOnsPath, "Locked", "Locked.toc"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var addon = Assert.Single(LocalAddons.Scan(_addOnsPath, []));
        Assert.Equal("Open", addon.FolderName);
    }

    [Fact]
    public void Scan_Removable_OnlyWhenEveryFolderHasAnExactToc()
    {
        WriteAddon("Suite", "## Title: Suite\n");
        WriteAddon("Suite-Module", "## Title: Module\n## Dependencies: Suite\n", "Suite-Module_Vanilla.toc");
        WriteAddon("Plain", "## Title: Plain\n");

        var addons = LocalAddons.Scan(_addOnsPath, []);

        Assert.False(addons.Single(a => a.FolderName == "Suite").Removable);
        Assert.True(addons.Single(a => a.FolderName == "Plain").Removable);
    }

    [Fact]
    public void Scan_StripsColourCodesFromTitle()
    {
        WriteAddon("Colourful", "## Title: |cff00ccffColourful|r Addon\n");

        var addon = Assert.Single(LocalAddons.Scan(_addOnsPath, []));
        Assert.Equal("Colourful Addon", addon.Name);
    }

    [Fact]
    public void ReadDeclaredIds_ReadsTopLevelTocOnly_IgnoringEmbeddedLibraryToc()
    {
        WriteAddon("RXPGuides", "## Title: RXPGuides\n## X-Curse-Project-ID: 486246\n## X-Wago-ID: rkGrgw6y\n");
        var library = Path.Combine(_addOnsPath, "RXPGuides", "libs", "HereBeDragons");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "HereBeDragons.toc"), "## X-Curse-Project-ID: 94348\n");

        Assert.Equal(new DeclaredAddonIds(486246, "rkGrgw6y", null), LocalAddons.ReadDeclaredIds(_addOnsPath, "RXPGuides"));
    }

    [Fact]
    public void ReadDeclaredIds_FallsBackToFlavourToc_AndReadsWowInterfaceId()
    {
        WriteAddon("BugSack", "## X-Curse-Project-ID: 6273\n## X-WoWI-ID: 5460\n", "BugSack_Vanilla.toc");

        Assert.Equal(new DeclaredAddonIds(6273, null, "5460"), LocalAddons.ReadDeclaredIds(_addOnsPath, "BugSack"));
    }

    [Fact]
    public void ReadDeclaredIds_NoTopLevelToc_ReturnsNull()
    {
        var library = Path.Combine(_addOnsPath, "Loose", "libs", "Lib");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "Lib.toc"), "## X-Curse-Project-ID: 94348\n");

        Assert.Null(LocalAddons.ReadDeclaredIds(_addOnsPath, "Loose"));
        Assert.Null(LocalAddons.ReadDeclaredIds(_addOnsPath, "Missing"));
    }

    [Fact]
    public void ReadDeclaredIds_NonNumericCurseId_IsNull()
    {
        WriteAddon("Odd", "## X-Curse-Project-ID: abc\n");

        Assert.Equal(new DeclaredAddonIds(null, null, null), LocalAddons.ReadDeclaredIds(_addOnsPath, "Odd"));
    }
}
