using System.Security.Cryptography;
using System.Text;

namespace Steward.Core.Tests;

public sealed class StewardGuidesAddonTests : IDisposable
{
    private const string Interface = "11509, 50504, 120100, 16001";

    private readonly string _root = Directory.CreateTempSubdirectory("steward-guides-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static (string Name, string Text, string? Tag, long UpdatedAt)[] SampleGuides() =>
    [
        ("Forever Leveling Guide - Both Factions", "83|1084041902:payload%|40000", "Buyer#1234", 100),
        ("Mists of Pandaria Guide - Bundle", "159|2792083552:other%|40000", null, 200),
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

    private static string GuidesLuaPath(string addOnsPath) => Path.Combine(addOnsPath, "StewardGuides", "Guides.lua");

    private static string TocPath(string addOnsPath) => Path.Combine(addOnsPath, "StewardGuides", "StewardGuides.toc");

    private static string WithMismatchedBootstrapHash(string lua)
    {
        const string bootstrapMarker = "local bootstrap = \"";
        var hashStart = lua.IndexOf(bootstrapMarker, StringComparison.Ordinal) + bootstrapMarker.Length;
        var dummyHash = new string('0', 64);
        var replaced = lua[..hashStart] + dummyHash + lua[(hashStart + 64)..];

        var afterStart = replaced.IndexOf(bootstrapMarker, StringComparison.Ordinal);
        var newFingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(replaced[afterStart..])));

        const string fingerprintMarker = "local fingerprint = \"";
        var fpStart = replaced.IndexOf(fingerprintMarker, StringComparison.Ordinal) + fingerprintMarker.Length;
        return replaced[..fpStart] + newFingerprint + replaced[(fpStart + 64)..];
    }

    [Fact]
    public void Toc_CarriesTheDirectivesTheClientNeeds()
    {
        var lines = StewardGuidesAddon.Toc(Interface).Split('\n');

        Assert.Equal($"## Interface: {Interface}", lines[0]);
        Assert.Equal("## Title: Steward Guides", lines[1]);
        Assert.Equal("## Group: RXPGuides", lines[2]);
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

        Assert.StartsWith("local generation = 1758380000000\nlocal fingerprint = \"", lua, StringComparison.Ordinal);
        Assert.Contains("local bootstrap = \"", lua, StringComparison.Ordinal);
        Assert.Contains(
            "    { name = \"Forever Leveling Guide - Both Factions\", text = [==[83|1084041902:payload%|40000]==], tag = \"Buyer#1234\", updatedAt = 100 },",
            lua,
            StringComparison.Ordinal);
        Assert.Contains(
            "    { name = \"Mists of Pandaria Guide - Bundle\", text = [==[159|2792083552:other%|40000]==], updatedAt = 200 },",
            lua,
            StringComparison.Ordinal);
        Assert.Contains("rxp.guideImporter:ImportString(guide.text)", lua, StringComparison.Ordinal);
        Assert.Contains("StewardGuidesDB = StewardGuidesDB or { imported = {}, status = {} }", lua, StringComparison.Ordinal);
        Assert.Contains("if hash and AlreadyLoaded(rxp, StewardGuidesDB.keys[KeysId(hash)]) then", lua, StringComparison.Ordinal);
        Assert.Contains("StewardGuidesDB.generation = generation", lua, StringComparison.Ordinal);
        Assert.Contains("\"IsJunkIconEnabled\", \"GetModKey\"", lua, StringComparison.Ordinal);
        Assert.Contains("Guides Loaded Successfully", lua, StringComparison.Ordinal);
        Assert.Contains("StewardGuidesDB.status[hash] = message", lua, StringComparison.Ordinal);
        Assert.Contains("local _, tag = BNGetInfo()", lua, StringComparison.Ordinal);
        Assert.Contains("bought on \" .. guide.tag .. \", you are \" .. playerTag .. \"; not imported", lua, StringComparison.Ordinal);
        Assert.Contains("C_ChatInfo.SendAddonMessage(\"HoobiVersion\", addonName .. \"=\"", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NeverProducesCarriageReturns() =>
        Assert.DoesNotContain('\r', StewardGuidesAddon.Render(SampleGuides(), 1));

    [Fact]
    public void Render_FingerprintRoundTripsThroughARewrite()
    {
        var addOnsPath = InstallRxpGuides();
        Assert.Equal(StewardGuidesWriteOutcome.Written, StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1).Outcome);

        var second = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2);

        Assert.Equal(StewardGuidesWriteOutcome.Skipped, second.Outcome);
        Assert.Equal(1, second.Generation);
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
            () => StewardGuidesAddon.Render([("Broken", "83|1:pay]==]load%|40000", null, 1L)], 1));
    }

    [Fact]
    public void Render_EscapesQuotesAndBackslashesInNames()
    {
        var lua = StewardGuidesAddon.Render([(@"A ""B"" \ C", "1|2:x", (string?)null, 1L)], 1);

        Assert.Contains(@"name = ""A \""B\"" \\ C""", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_CreatesTheAddonFolderWithTheInterfaceNumbersFromRxpGuides()
    {
        var addOnsPath = InstallRxpGuides();

        var result = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1758380000000);

        Assert.Equal(StewardGuidesWriteOutcome.Written, result.Outcome);
        Assert.Equal(1758380000000, result.Generation);
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

        StewardGuidesAddon.Write(
            addOnsPath, [("Forever Leveling Guide - Both Factions", "83|1084041902:payload%|40000", (string?)null, 100L)], 2);

        var lua = File.ReadAllText(GuidesLuaPath(addOnsPath));
        Assert.DoesNotContain("Mists of Pandaria", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_KeepsTheGenerationWhenNothingChanged()
    {
        var addOnsPath = InstallRxpGuides();
        Assert.Equal(1, StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1).Generation);

        var unchanged = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2);
        Assert.Equal(StewardGuidesWriteOutcome.Skipped, unchanged.Outcome);
        Assert.Equal(1, unchanged.Generation);

        var changed = StewardGuidesAddon.Write(
            addOnsPath, [("Forever Leveling Guide - Both Factions", "83|1084041902:payload%|40000", (string?)null, 100L)], 3);
        Assert.Equal(StewardGuidesWriteOutcome.Written, changed.Outcome);
        Assert.Equal(3, changed.Generation);

        var lua = File.ReadAllText(GuidesLuaPath(addOnsPath));
        Assert.StartsWith("local generation = 3\n", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Rewrites_WhenAProductsUpdatedAtChanges()
    {
        var addOnsPath = InstallRxpGuides();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        var bumped = SampleGuides();
        bumped[0] = (bumped[0].Name, bumped[0].Text, bumped[0].Tag, 999L);

        var result = StewardGuidesAddon.Write(addOnsPath, bumped, 2);

        Assert.Equal(StewardGuidesWriteOutcome.Written, result.Outcome);
        Assert.Equal(2, result.Generation);
    }

    [Fact]
    public void Write_Rewrites_WhenOnlyTheTocOrIconIsStale()
    {
        var addOnsPath = InstallRxpGuides();
        Assert.Equal(1, StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1).Generation);

        InstallRxpGuides("11510");
        var afterInterfaceChange = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2);
        Assert.Equal(StewardGuidesWriteOutcome.Written, afterInterfaceChange.Outcome);
        Assert.Contains("## Interface: 11510", File.ReadAllText(TocPath(addOnsPath)), StringComparison.Ordinal);

        File.Delete(Path.Combine(addOnsPath, "StewardGuides", "Icon.tga"));
        var afterIconDeleted = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 3);
        Assert.Equal(StewardGuidesWriteOutcome.Written, afterIconDeleted.Outcome);
        Assert.True(File.Exists(Path.Combine(addOnsPath, "StewardGuides", "Icon.tga")));

        File.WriteAllText(TocPath(addOnsPath), File.ReadAllText(TocPath(addOnsPath)).Replace("## Group: RXPGuides", "## Category: Hoobi", StringComparison.Ordinal));
        var afterTocLineChange = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 4);
        Assert.Equal(StewardGuidesWriteOutcome.Written, afterTocLineChange.Outcome);
        Assert.Contains("## Group: RXPGuides", File.ReadAllText(TocPath(addOnsPath)), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Skips_WhenOnlyTheTocVersionLineDiffers()
    {
        var addOnsPath = InstallRxpGuides();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        var tocPath = TocPath(addOnsPath);
        var tamperedToc = File.ReadAllText(tocPath).ReplaceLineEndings("\n").Split('\n')
            .Select(line => line.StartsWith("## Version:", StringComparison.Ordinal) ? "## Version: 0.0.0-tampered" : line);
        File.WriteAllText(tocPath, string.Join('\n', tamperedToc));

        var result = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2);

        Assert.Equal(StewardGuidesWriteOutcome.Skipped, result.Outcome);
        Assert.Equal(1, result.Generation);
        Assert.Contains("## Version: 0.0.0-tampered", File.ReadAllText(tocPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ReportsChangedOnDisk_WhenTheBodyWasEditedOutsideSteward_AndDoesNotRewrite()
    {
        var addOnsPath = InstallRxpGuides();
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
        var addOnsPath = InstallRxpGuides();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        var guidesPath = GuidesLuaPath(addOnsPath);
        File.WriteAllText(guidesPath, File.ReadAllText(guidesPath).Replace("payload", "payloae", StringComparison.Ordinal));

        var forced = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2, force: true);

        Assert.Equal(StewardGuidesWriteOutcome.Written, forced.Outcome);
        Assert.Equal(2, forced.Generation);
        Assert.DoesNotContain("payloae", File.ReadAllText(guidesPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Rewrites_WhenAnOlderBuildsBootstrapHashDiffers()
    {
        var addOnsPath = InstallRxpGuides();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        var guidesPath = GuidesLuaPath(addOnsPath);
        File.WriteAllText(guidesPath, WithMismatchedBootstrapHash(File.ReadAllText(guidesPath)));

        var result = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2);

        Assert.Equal(StewardGuidesWriteOutcome.Written, result.Outcome);
        Assert.Equal(2, result.Generation);
    }

    [Fact]
    public void Write_WritesOnce_WhenTheOnDiskFileHasNoFingerprintHeader()
    {
        var addOnsPath = InstallRxpGuides();
        StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 1);

        File.WriteAllText(GuidesLuaPath(addOnsPath), "local generation = 1\nlocal guides = {}\n");

        var result = StewardGuidesAddon.Write(addOnsPath, SampleGuides(), 2);

        Assert.Equal(StewardGuidesWriteOutcome.Written, result.Outcome);
        Assert.Equal(2, result.Generation);
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
