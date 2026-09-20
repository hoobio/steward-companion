using System.Reflection;
using System.Text;

namespace Steward.Core;

public static class StewardGuidesAddon
{
    public const string FolderName = "StewardGuides";

    private const string Terminator = "]==]";
    private const string TitleLine = "## Title: Steward Guides";
    private const string AuthorLine = "## Author: Hoobi";

    private const string Bootstrap = """

        local frame = CreateFrame("Frame")
        local index = 0

        local function Say(text)
            print("|cff409fffSteward|r " .. text)
        end

        local function Hash(text)
            return text:match("^%d+|([^:]+):")
        end

        local function ImportNext(rxp)
            index = index + 1
            local guide = guides[index]
            if not guide then
                Say("all guide strings handed to RXPGuides")
                return
            end
            local hash = Hash(guide.text)
            if hash and StewardGuidesDB.imported[hash] then
                ImportNext(rxp)
                return
            end
            local ok, err = rxp.guideImporter:ImportString(guide.text)
            Say(guide.name .. ": " .. (ok and "importing" or ("rejected: " .. tostring(err))))
            local function WaitForIdle()
                if rxp.guideImporter.importCoroutine == nil and (rxp.guideImporter.importBufferSize or 0) == 0 then
                    local history = rxp.guideImporter.gui and rxp.guideImporter.gui.importStatusHistory
                    local status = history and history[1]
                    if hash and type(status) == "string" and status:find("Guides Loaded Successfully", 1, true) == 1 then
                        StewardGuidesDB.imported[hash] = true
                    end
                    ImportNext(rxp)
                else
                    C_Timer.After(1, WaitForIdle)
                end
            end
            C_Timer.After(1, WaitForIdle)
        end

        frame:RegisterEvent("PLAYER_ENTERING_WORLD")
        frame:SetScript("OnEvent", function(self)
            self:UnregisterAllEvents()
            StewardGuidesDB = StewardGuidesDB or { imported = {} }
            StewardGuidesDB.imported = StewardGuidesDB.imported or {}
            C_Timer.After(3, function()
                local rxp = LibStub("AceAddon-3.0"):GetAddon("RXPGuides", true)
                if not rxp or not rxp.guideImporter or not rxp.guideImporter.ImportString then
                    Say("RXPGuides importer not found; nothing imported")
                    return
                end
                ImportNext(rxp)
            end)
        end)

        """;

    public static string Toc(string interfaceNumbers) => string.Join('\n',
        $"## Interface: {interfaceNumbers}",
        TitleLine,
        "## Category: Hoobi",
        "## Notes: Purchased RestedXP guides, kept current by the Steward desktop app. Nothing to configure here.",
        AuthorLine,
        @"## IconTexture: Interface\AddOns\StewardGuides\Icon",
        "## Dependencies: RXPGuides",
        "## SavedVariables: StewardGuidesDB",
        $"## Version: {Version()}",
        string.Empty,
        "Guides.lua",
        string.Empty);

    public static string Render(IReadOnlyList<(string Name, string Text)> guides)
    {
        ArgumentNullException.ThrowIfNull(guides);

        var builder = new StringBuilder("local guides = {\n");
        foreach (var (name, text) in guides)
        {
            if (text.Contains(Terminator, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{name} contains the long-bracket terminator {Terminator}");
            }

            var escaped = name.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
            builder.Append("    { name = \"").Append(escaped).Append("\", text = [==[").Append(text.Trim()).Append("]==] },\n");
        }

        return builder.Append("}\n").Append(Bootstrap).ToString();
    }

    public static void Write(string addOnsPath, IReadOnlyList<(string Name, string Text)> guides)
    {
        var lua = Render(guides);
        var rxpTocPath = Path.Combine(addOnsPath, "RXPGuides", "RXPGuides.toc");
        var toc = Toc(TocFile.ReadDirective(rxpTocPath, "Interface")
            ?? throw new InvalidOperationException($"{rxpTocPath} has no ## Interface line; RXPGuides must be installed first"));

        var folder = Path.GetFullPath(Path.Combine(addOnsPath, FolderName));
        if (folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("WTF", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write a path containing a WTF segment: {folder}");
        }

        if (Directory.Exists(folder))
        {
            var tocPath = Path.Combine(folder, $"{FolderName}.toc");
            if (!File.Exists(tocPath) || !IsOurs(File.ReadAllLines(tocPath)))
            {
                throw new InvalidOperationException($"{folder} was not written by Steward; refusing to replace it");
            }

            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        WriteFile(Path.Combine(folder, $"{FolderName}.toc"), Encoding.UTF8.GetBytes(toc));
        WriteFile(Path.Combine(folder, "Icon.tga"), Icon());
        WriteFile(Path.Combine(folder, "Guides.lua"), Encoding.UTF8.GetBytes(lua));
    }

    private static bool IsOurs(string[] tocLines) =>
        tocLines.Contains(TitleLine, StringComparer.Ordinal) && tocLines.Contains(AuthorLine, StringComparer.Ordinal);

    private static void WriteFile(string target, byte[] content)
    {
        var tempPath = target + ".tmp";
        File.WriteAllBytes(tempPath, content);
        File.Move(tempPath, target, overwrite: true);
    }

    private static byte[] Icon()
    {
        using var stream = typeof(StewardGuidesAddon).Assembly.GetManifestResourceStream("Steward.Core.Assets.Icon.tga")
            ?? throw new InvalidOperationException("Steward.Core.Assets.Icon.tga is missing from the assembly");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Version() =>
        typeof(StewardGuidesAddon).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";
}
