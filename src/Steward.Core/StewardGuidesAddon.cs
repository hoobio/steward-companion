using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Steward.Core;

public static partial class StewardGuidesAddon
{
    public const string FolderName = "StewardGuides";

    private const string Terminator = "]==]";
    private const string TitleLine = "## Title: Steward Guides";
    private const string AuthorLine = "## Author: Hoobi";

    private const string Bootstrap = """

        local frame = CreateFrame("Frame")
        local index = 0
        local playerTag
        local Import

        local function Say(text)
            print("|cff409fffSteward|r " .. text)
        end

        local function Hash(text)
            return text:match("^%d+|([^:]+):")
        end

        local function Reject(guide, hash, message)
            Say(guide.name .. ": " .. message)
            if hash then
                StewardGuidesDB.status[hash] = message
            end
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
            if guide.tag and guide.tag:lower() ~= playerTag:lower() then
                Reject(guide, hash, "bought on " .. guide.tag .. ", you are " .. playerTag .. "; not imported")
                ImportNext(rxp)
                return
            end
            Import(rxp, guide, hash, false)
        end

        function Import(rxp, guide, hash, retried)
            local ok, err = rxp.guideImporter:ImportString(guide.text)
            Say(guide.name .. ": " .. (ok and "importing" or ("rejected: " .. tostring(err))))
            local function WaitForIdle()
                if rxp.guideImporter.importCoroutine ~= nil or (rxp.guideImporter.importBufferSize or 0) ~= 0 then
                    C_Timer.After(1, WaitForIdle)
                    return
                end
                local history = rxp.guideImporter.gui and rxp.guideImporter.gui.importStatusHistory
                local status = history and history[1]
                if type(status) == "string" and status:find("Guides Loaded Successfully", 1, true) == 1 then
                    if hash then
                        StewardGuidesDB.imported[hash] = true
                        StewardGuidesDB.status[hash] = nil
                    end
                elseif type(status) == "string" then
                    if not retried and status:find("restart your game client", 1, true) then
                        C_Timer.After(10, function() Import(rxp, guide, hash, true) end)
                        return
                    end
                    Reject(guide, hash, status)
                end
                ImportNext(rxp)
            end
            C_Timer.After(1, WaitForIdle)
        end

        local function Start(rxp, attempts)
            local _, tag = BNGetInfo()
            playerTag = tag
            if tag then
                ImportNext(rxp)
            elseif attempts < 6 then
                C_Timer.After(5, function() Start(rxp, attempts + 1) end)
            else
                Say("Battle.net is not connected; guides skipped")
            end
        end

        local function Begin(rxp, attempts)
            if rxp.guideImporter and rxp.guideImporter.ImportString and rxp.settings and rxp.settings.profile then
                Start(rxp, 0)
            elseif attempts < 6 then
                C_Timer.After(5, function() Begin(rxp, attempts + 1) end)
            else
                Say("RXPGuides is not initialised; guides skipped")
            end
        end

        frame:RegisterEvent("PLAYER_ENTERING_WORLD")
        frame:SetScript("OnEvent", function(self)
            self:UnregisterAllEvents()
            StewardGuidesDB = StewardGuidesDB or { imported = {}, status = {} }
            StewardGuidesDB.imported = StewardGuidesDB.imported or {}
            StewardGuidesDB.status = StewardGuidesDB.status or {}
            StewardGuidesDB.generation = generation
            C_Timer.After(3, function()
                local rxp = LibStub("AceAddon-3.0"):GetAddon("RXPGuides", true)
                if not rxp then
                    Say("RXPGuides importer not found; nothing imported")
                    return
                end
                Begin(rxp, 0)
            end)
        end)

        """;

    public static string Toc(string interfaceNumbers) => string.Join('\n',
        $"## Interface: {interfaceNumbers}",
        TitleLine,
        "## Category: Hoobi",
        "## Notes: Purchased RestedXP guides, kept current by the Steward desktop app, with no settings of its own.",
        AuthorLine,
        @"## IconTexture: Interface\AddOns\StewardGuides\Icon",
        "## Dependencies: RXPGuides",
        "## SavedVariables: StewardGuidesDB",
        $"## Version: {Version()}",
        string.Empty,
        "Guides.lua",
        string.Empty);

    public static string? Hash(string guide)
    {
        ArgumentNullException.ThrowIfNull(guide);

        return GuideHeader().Match(guide) is { Success: true } match ? match.Groups[1].Value : null;
    }

    public static string Render(IReadOnlyList<(string Name, string Text, string? Tag)> guides, long generation)
    {
        ArgumentNullException.ThrowIfNull(guides);

        var builder = new StringBuilder("local generation = ")
            .Append(generation.ToString(CultureInfo.InvariantCulture))
            .Append("\nlocal guides = {\n");
        foreach (var (name, text, tag) in guides)
        {
            if (text.Contains(Terminator, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{name} contains the long-bracket terminator {Terminator}");
            }

            builder.Append("    { name = \"").Append(Quote(name)).Append("\", text = [==[").Append(text.Trim()).Append("]==]");
            if (tag is not null)
            {
                builder.Append(", tag = \"").Append(Quote(tag)).Append('"');
            }

            builder.Append(" },\n");
        }

        return builder.Append("}\n").Append(Bootstrap).ToString();
    }

    public static void Write(string addOnsPath, IReadOnlyList<(string Name, string Text, string? Tag)> guides, long generation)
    {
        var lua = Render(guides, generation);
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

    private static string Quote(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

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

    [GeneratedRegex(@"^\s*\d+\|([^:]+):")]
    private static partial Regex GuideHeader();

    private static string Version() =>
        typeof(StewardGuidesAddon).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";
}
