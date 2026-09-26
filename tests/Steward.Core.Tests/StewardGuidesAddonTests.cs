namespace Steward.Core.Tests;

public sealed class StewardGuidesAddonTests : IDisposable
{
    private const string Interface = "11509, 50504, 120100, 16001";

    private readonly string _root = Directory.CreateTempSubdirectory("steward-guides-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static (string Name, string Text, string? Tag)[] SampleGuides() =>
    [
        ("Forever Leveling Guide - Both Factions", "83|1084041902:payload%|40000", "Buyer#1234"),
        ("Mists of Pandaria Guide - Bundle", "159|2792083552:other%|40000", null),
    ];

    private string InstallRxpGuides(string interfaceValue = Interface)
    {
        var addOnsPath = Path.Combine(_root, "AddOns");
        var rxpPath = Path.Combine(addOnsPath, "RXPGuides");
        Directory.CreateDirectory(rxpPath);
        File.WriteAllText(
            Path.Combine(rxpPath, "RXPGuides.toc"),
            $"## Interface: {interfaceValue}\n## Title: RestedXP Guides\n## Version: v4.11.4\n");
        return addOnsPath;
    }

    [Fact]
    public void Toc_CarriesTheDirectivesTheClientNeeds()
    {
        var lines = StewardGuidesAddon.Toc(Interface).Split('\n');

        Assert.Equal($"## Interface: {Interface}", lines[0]);
        Assert.Equal("## Title: Steward Guides", lines[1]);
        Assert.Equal("## Category: Hoobi", lines[2]);
        Assert.Equal(
            "## Notes: Purchased RestedXP guides, kept current by the Steward desktop app, with no settings of its own.",
            lines[3]);
        Assert.Equal("## Author: Hoobi", lines[4]);
        Assert.Equal(@"## IconTexture: Interface\AddOns\StewardGuides\Icon", lines[5]);
        Assert.Equal("## Dependencies: RXPGuides", lines[6]);
        Assert.Equal("## SavedVariables: StewardGuidesDB", lines[7]);
        Assert.StartsWith("## Version: ", lines[8], StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[9]);
        Assert.Equal("Guides.lua", lines[10]);
    }

    [Fact]
    public void Render_WritesEachGuideAsALongBracketLiteralAndKeepsTheBootstrap()
    {
        var lua = StewardGuidesAddon.Render(SampleGuides(), 1758380000000);

        Assert.StartsWith("local generation = 1758380000000\nlocal guides = {\n", lua, StringComparison.Ordinal);
        Assert.Contains(
            "    { name = \"Forever Leveling Guide - Both Factions\", text = [==[83|1084041902:payload%|40000]==], tag = \"Buyer#1234\" },",
            lua,
            StringComparison.Ordinal);
        Assert.Contains(
            "    { name = \"Mists of Pandaria Guide - Bundle\", text = [==[159|2792083552:other%|40000]==] },",
            lua,
            StringComparison.Ordinal);
        Assert.Contains("rxp.guideImporter:ImportString(guide.text)", lua, StringComparison.Ordinal);
        Assert.Contains("StewardGuidesDB = StewardGuidesDB or { imported = {}, status = {} }", lua, StringComparison.Ordinal);
        Assert.Contains("StewardGuidesDB.generation = generation", lua, StringComparison.Ordinal);
        Assert.Contains("\"IsJunkIconEnabled\", \"GetModKey\"", lua, StringComparison.Ordinal);
        Assert.Contains("Guides Loaded Successfully", lua, StringComparison.Ordinal);
        Assert.Contains("StewardGuidesDB.status[hash] = message", lua, StringComparison.Ordinal);
        Assert.Contains("local _, tag = BNGetInfo()", lua, StringComparison.Ordinal);
        Assert.Contains("bought on \" .. guide.tag .. \", you are \" .. playerTag .. \"; not imported", lua, StringComparison.Ordinal);
        Assert.Contains("C_ChatInfo.SendAddonMessage(\"HoobiVersion\", addonName .. \"=\"", lua, StringComparison.Ordinal);
    }

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
            () => StewardGuidesAddon.Render([("Broken", "83|1:pay]==]load%|40000", null)], 1));
    }

    [Fact]
    public void Render_EscapesQuotesAndBackslashesInNames()
    {
        var lua = StewardGuidesAddon.Render([(@"A ""B"" \ C", "1|2:x", null)], 1);

        Assert.Contains(@"name = ""A \""B\"" \\ C""", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_CreatesTheAddonFolderWithTheInterfaceNumbersFromRxpGuides()
    {
        var addOnsPath = InstallRxpGuides();

        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1758380000000);

        var folder = Path.Combine(addOnsPath, "StewardGuides");
        Assert.Contains($"## Interface: {Interface}", File.ReadAllText(Path.Combine(folder, "StewardGuides.toc")), StringComparison.Ordinal);
        Assert.Contains("[==[83|1084041902:payload%|40000]==]", File.ReadAllText(Path.Combine(folder, "Guides.lua")), StringComparison.Ordinal);
        Assert.Equal(16402, new FileInfo(Path.Combine(folder, "Icon.tga")).Length);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void Write_ReplacesTheFolderItWroteBefore()
    {
        var addOnsPath = InstallRxpGuides();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        StewardGuidesAddon.Write(addOnsPath, [("Forever Leveling Guide - Both Factions", "83|1084041902:payload%|40000", null)], 2);

        var lua = File.ReadAllText(Path.Combine(addOnsPath, "StewardGuides", "Guides.lua"));
        Assert.DoesNotContain("Mists of Pandaria", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_KeepsTheGenerationWhenTheBodyIsUnchanged()
    {
        var addOnsPath = InstallRxpGuides();
        Assert.Equal(1, StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1));

        Assert.Equal(1, StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2));
        Assert.Equal(3, StewardGuidesAddon.Write(addOnsPath, [("Forever Leveling Guide - Both Factions", "83|1084041902:payload%|40000", null)], 3));

        var lua = File.ReadAllText(Path.Combine(addOnsPath, "StewardGuides", "Guides.lua"));
        Assert.StartsWith("local generation = 3\n", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Throws_WhenTheFolderIsNotOurs()
    {
        var addOnsPath = InstallRxpGuides();
        var folder = Path.Combine(addOnsPath, "StewardGuides");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "StewardGuides.toc"), "## Title: Someone else\n## Author: Someone else\n");

        Assert.Throws<InvalidOperationException>(() => StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1));

        Assert.False(File.Exists(Path.Combine(folder, "Guides.lua")));
    }

    [Fact]
    public void Write_Throws_WhenRxpGuidesIsNotInstalled()
    {
        var addOnsPath = Path.Combine(_root, "AddOns");
        Directory.CreateDirectory(addOnsPath);

        Assert.Throws<InvalidOperationException>(() => StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1));
    }

    [Fact]
    public void Write_Throws_ForAnAddOnsPathContainingAWtfSegment()
    {
        var addOnsPath = Path.Combine(_root, "WTF", "AddOns");
        var rxpPath = Path.Combine(addOnsPath, "RXPGuides");
        Directory.CreateDirectory(rxpPath);
        File.WriteAllText(Path.Combine(rxpPath, "RXPGuides.toc"), $"## Interface: {Interface}\n");

        Assert.Throws<InvalidOperationException>(() => StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1));

        Assert.False(Directory.Exists(Path.Combine(addOnsPath, "StewardGuides")));
    }
}
