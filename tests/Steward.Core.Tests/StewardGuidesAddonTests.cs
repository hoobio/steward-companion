namespace Steward.Core.Tests;

public sealed class StewardGuidesAddonTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-guides-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static (string Name, string Text, string? Tag, long UpdatedAt)[] SampleGuides() =>
    [
        ("Forever Leveling Guide - Both Factions", "83|1084041902:payload%|40000", "Buyer#1234", 100),
        ("Mists of Pandaria Guide - Bundle", "159|2792083552:other%|40000", null, 200),
    ];

    private string InstallPublished(string title = "## Title: Steward: Guides")
    {
        var addOnsPath = Path.Combine(_root, "AddOns");
        var folder = Path.Combine(addOnsPath, "StewardGuides");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "StewardGuides.toc"), $"## Interface: 16001\n{title}\n## Author: Hoobi\n\nGuides.lua\nCore.lua\n");
        File.WriteAllText(GuidesLuaPath(addOnsPath), "local _, ns = ...\nns.generation = 0\nns.guides = {}\n");
        return addOnsPath;
    }

    private static string GuidesLuaPath(string addOnsPath) => Path.Combine(addOnsPath, "StewardGuides", "Guides.lua");

    [Fact]
    public void Render_WritesEachGuideAsALongBracketLiteralThenHandsThemToTheAddon()
    {
        var lua = StewardGuidesAddon.Render(SampleGuides(), 1758380000000);

        Assert.StartsWith("local generation = 1758380000000\nlocal fingerprint = \"", lua, StringComparison.Ordinal);
        Assert.Contains("\"\nlocal guides = {\n", lua, StringComparison.Ordinal);
        Assert.Contains(
            "    { name = \"Forever Leveling Guide - Both Factions\", text = [==[83|1084041902:payload%|40000]==], tag = \"Buyer#1234\", updatedAt = 100 },",
            lua,
            StringComparison.Ordinal);
        Assert.Contains(
            "    { name = \"Mists of Pandaria Guide - Bundle\", text = [==[159|2792083552:other%|40000]==], updatedAt = 200 },",
            lua,
            StringComparison.Ordinal);
        Assert.EndsWith("}\nlocal _, ns = ...\nns.generation = generation\nns.guides = guides\n", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap", lua, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_NeverProducesCarriageReturns() =>
        Assert.DoesNotContain('\r', StewardGuidesAddon.Render(SampleGuides(), 1));

    [Theory]
    [InlineData("83|1084041902:payload%|40000", "1084041902")]
    [InlineData("\n159|2792083552:other%|40000", "2792083552")]
    [InlineData("no header here", null)]
    public void Hash_TakesTheNumberBetweenTheBarAndTheColon(string guide, string? expected) =>
        Assert.Equal(expected, StewardGuidesAddon.Hash(guide));

    [Fact]
    public void Render_Throws_WhenAGuideHoldsTheTerminator()
    {
        Assert.Throws<InvalidOperationException>(
            () => StewardGuidesAddon.Render([("Broken", "83|1:pay]==]load%|40000", null, 1L)], 1));
    }

    [Fact]
    public void Render_EscapesQuotesAndBackslashesInNames()
    {
        var lua = StewardGuidesAddon.Render([(@"A ""B"" \ C", "1|2:x", (string?)null, 1L)], 1);

        Assert.Contains(@"name = ""A \""B\"" \\ C""", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ReplacesThePlaceholderGuidesFileInThePublishedAddon()
    {
        var addOnsPath = InstallPublished();

        var result = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1758380000000);

        Assert.Equal(StewardGuidesWriteOutcome.Written, result.Outcome);
        Assert.Equal(1758380000000, result.Generation);
        Assert.Contains("[==[83|1084041902:payload%|40000]==]", File.ReadAllText(GuidesLuaPath(addOnsPath)), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(addOnsPath, "StewardGuides"), "*.tmp"));
    }

    [Fact]
    public void Write_LeavesTheOldGeneratedFolderUntouched()
    {
        var addOnsPath = InstallPublished("## Title: Steward Guides");
        var before = File.ReadAllText(GuidesLuaPath(addOnsPath));

        var result = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        Assert.Equal(StewardGuidesWriteOutcome.NotInstalled, result.Outcome);
        Assert.Equal(before, File.ReadAllText(GuidesLuaPath(addOnsPath)));
    }

    [Fact]
    public void Write_ReportsNotInstalled_WhenTheFolderIsMissing()
    {
        var addOnsPath = Path.Combine(_root, "AddOns");
        Directory.CreateDirectory(addOnsPath);

        Assert.Equal(StewardGuidesWriteOutcome.NotInstalled, StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1).Outcome);
        Assert.False(Directory.Exists(Path.Combine(addOnsPath, "StewardGuides")));
    }

    [Fact]
    public void Write_KeepsTheGenerationWhenNothingChanged()
    {
        var addOnsPath = InstallPublished();
        Assert.Equal(1, StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1).Generation);

        var unchanged = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2);
        Assert.Equal(StewardGuidesWriteOutcome.Skipped, unchanged.Outcome);
        Assert.Equal(1, unchanged.Generation);

        var changed = StewardGuidesAddon.Write(
            addOnsPath, [("Forever Leveling Guide - Both Factions", "83|1084041902:payload%|40000", (string?)null, 100L)], 3);
        Assert.Equal(StewardGuidesWriteOutcome.Written, changed.Outcome);
        Assert.Equal(3, changed.Generation);
        Assert.DoesNotContain("Mists of Pandaria", File.ReadAllText(GuidesLuaPath(addOnsPath)), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Rewrites_WhenAProductsUpdatedAtChanges()
    {
        var addOnsPath = InstallPublished();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        var bumped = SampleGuides();
        bumped[0] = (bumped[0].Name, bumped[0].Text, bumped[0].Tag, 999L);

        var result = StewardGuidesAddon.Write(addOnsPath, bumped, 2);

        Assert.Equal(StewardGuidesWriteOutcome.Written, result.Outcome);
        Assert.Equal(2, result.Generation);
    }

    [Fact]
    public void Write_ReportsChangedOnDisk_WhenTheBodyWasEditedOutsideSteward_AndDoesNotRewrite()
    {
        var addOnsPath = InstallPublished();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        var guidesPath = GuidesLuaPath(addOnsPath);
        var tampered = File.ReadAllText(guidesPath).Replace("payload", "payloae", StringComparison.Ordinal);
        File.WriteAllText(guidesPath, tampered);

        var result = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2);

        Assert.Equal(StewardGuidesWriteOutcome.ChangedOnDisk, result.Outcome);
        Assert.Equal(1, result.Generation);
        Assert.Equal(tampered, File.ReadAllText(guidesPath));
    }

    [Fact]
    public void Write_Force_RewritesEvenWhenChangedOnDisk()
    {
        var addOnsPath = InstallPublished();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        var guidesPath = GuidesLuaPath(addOnsPath);
        File.WriteAllText(guidesPath, File.ReadAllText(guidesPath).Replace("payload", "payloae", StringComparison.Ordinal));

        var forced = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2, force: true);

        Assert.Equal(StewardGuidesWriteOutcome.Written, forced.Outcome);
        Assert.Equal(2, forced.Generation);
        Assert.DoesNotContain("payloae", File.ReadAllText(guidesPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Throws_ForAnAddOnsPathContainingAWtfSegment()
    {
        var addOnsPath = Path.Combine(_root, "WTF", "AddOns");
        Directory.CreateDirectory(addOnsPath);

        Assert.Throws<InvalidOperationException>(() => StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1));
    }
}
