using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Steward.Core;

public enum StewardGuidesWriteOutcome
{
    Written,
    Skipped,
    ChangedOnDisk,
}

public readonly record struct StewardGuidesWriteResult(StewardGuidesWriteOutcome Outcome, long Generation);

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

        local function Guard(rxp)
            local inventory = rxp.inventoryManager
            if not inventory then
                return
            end
            -- ponytail: RXPGuides v4.11.x reads settings.profile from its bag hook even when its own initialisation never ran; answer "off" until the profile exists
            for _, name in ipairs({ "IsRightClickEnabled", "IsBagAutomationEnabled", "IsMerchantAutomationEnabled", "IsJunkIconEnabled", "GetModKey" }) do
                local original = inventory[name]
                if type(original) == "function" then
                    inventory[name] = function(...)
                        if not (rxp.settings and rxp.settings.profile) then
                            return false
                        end
                        return original(...)
                    end
                end
            end
        end

        do
            local rxp = LibStub("AceAddon-3.0"):GetAddon("RXPGuides", true)
            if rxp then
                Guard(rxp)
            end
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
            if guide.tag and guide.tag:lower() ~= playerTag:lower() then
                Reject(guide, hash, "bought on " .. guide.tag .. ", you are " .. playerTag .. "; not imported")
                ImportNext(rxp)
                return
            end
            Import(rxp, guide, hash, false)
        end

        function Import(rxp, guide, hash, retried)
            if rxp.guideImporter.gui then
                rxp.guideImporter.gui.importStatusHistory = {}
            end
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
                local message = "Battle.net is not connected, so no guides were imported. Open your friends list to check, make sure the Battle.net desktop app is up to date and connected, then /reload."
                Say(message)
                for _, guide in ipairs(guides) do
                    local hash = Hash(guide.text)
                    if hash then
                        StewardGuidesDB.status[hash] = message
                    end
                end
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

        local addonName = ...
        C_ChatInfo.RegisterAddonMessagePrefix("HoobiVersion")
        local versionFrame = CreateFrame("Frame")
        versionFrame:RegisterEvent("CHAT_MSG_ADDON")
        versionFrame:SetScript("OnEvent", function(_, _, prefix, text, _, sender)
            if prefix ~= "HoobiVersion" or text ~= "ping" then return end
            C_ChatInfo.SendAddonMessage("HoobiVersion", addonName .. "=" .. (C_AddOns.GetAddOnMetadata(addonName, "Version") or "?"), "WHISPER", sender)
        end)

        """;

    // Bootstrap is a raw string literal, so its newlines follow the source checkout's line endings; normalise so every build hashes and renders the same bytes.
    private static readonly string NormalizedBootstrap = Bootstrap.ReplaceLineEndings("\n");
    private static readonly string BootstrapHash = Sha256Hex(NormalizedBootstrap);

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

    public static string Render(IReadOnlyList<(string Name, string Text, string? Tag, long UpdatedAt)> guides, long generation)
    {
        ArgumentNullException.ThrowIfNull(guides);

        var body = new StringBuilder("local guides = {\n");
        foreach (var (name, text, tag, updatedAt) in guides)
        {
            if (text.Contains(Terminator, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{name} contains the long-bracket terminator {Terminator}");
            }

            body.Append("    { name = \"").Append(Quote(name)).Append("\", text = [==[").Append(text.Trim()).Append("]==]");
            if (tag is not null)
            {
                body.Append(", tag = \"").Append(Quote(tag)).Append('"');
            }

            body.Append(", updatedAt = ").Append(updatedAt.ToString(CultureInfo.InvariantCulture)).Append(" },\n");
        }

        body.Append("}\n");

        var after = new StringBuilder("local bootstrap = \"")
            .Append(BootstrapHash)
            .Append("\"\n")
            .Append(body)
            .Append(NormalizedBootstrap)
            .ToString();

        var fingerprint = Sha256Hex(after);

        return new StringBuilder("local generation = ")
            .Append(generation.ToString(CultureInfo.InvariantCulture))
            .Append('\n')
            .Append("local fingerprint = \"")
            .Append(fingerprint)
            .Append("\"\n")
            .Append(after)
            .ToString()
            .ReplaceLineEndings("\n");
    }

    public static StewardGuidesWriteResult Write(
        string addOnsPath, IReadOnlyList<(string Name, string Text, string? Tag, long UpdatedAt)> guides, long generation, bool force = false)
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

        if (!force && Inspect(folder, toc, guides) is { } existing)
        {
            return existing;
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
        return new StewardGuidesWriteResult(StewardGuidesWriteOutcome.Written, generation);
    }

    private static StewardGuidesWriteResult? Inspect(
        string folder, string toc, IReadOnlyList<(string Name, string Text, string? Tag, long UpdatedAt)> guides)
    {
        var guidesPath = Path.Combine(folder, "Guides.lua");
        var tocPath = Path.Combine(folder, $"{FolderName}.toc");
        var iconPath = Path.Combine(folder, "Icon.tga");
        if (!File.Exists(guidesPath) || !File.Exists(tocPath) || !File.Exists(iconPath)
            || !File.ReadAllBytes(iconPath).AsSpan().SequenceEqual(Icon()))
        {
            return null;
        }

        var existingLua = File.ReadAllText(guidesPath);
        var header = HeaderLine().Match(existingLua);
        if (!header.Success)
        {
            return null;
        }

        var generation = long.Parse(header.Groups["generation"].Value, CultureInfo.InvariantCulture);
        var after = header.Groups["after"].Value;
        if (!string.Equals(Sha256Hex(after), header.Groups["fingerprint"].Value, StringComparison.OrdinalIgnoreCase))
        {
            return new StewardGuidesWriteResult(StewardGuidesWriteOutcome.ChangedOnDisk, generation);
        }

        if (!string.Equals(header.Groups["bootstrap"].Value, BootstrapHash, StringComparison.OrdinalIgnoreCase)
            || !EntriesMatch(after, guides))
        {
            return null;
        }

        if (!string.Equals(InterfaceLine(File.ReadAllText(tocPath)), InterfaceLine(toc), StringComparison.Ordinal))
        {
            return null;
        }

        return new StewardGuidesWriteResult(StewardGuidesWriteOutcome.Skipped, generation);
    }

    private static bool EntriesMatch(
        string after, IReadOnlyList<(string Name, string Text, string? Tag, long UpdatedAt)> guides)
    {
        var onDisk = GuideEntry().Matches(after)
            .Select(m => (Name: m.Groups["name"].Value, UpdatedAt: long.Parse(m.Groups["updatedAt"].Value, CultureInfo.InvariantCulture)))
            .ToHashSet();
        var wanted = guides.Select(g => (Name: Quote(g.Name), g.UpdatedAt)).ToHashSet();
        return onDisk.SetEquals(wanted);
    }

    private static string? InterfaceLine(string toc) => toc.ReplaceLineEndings("\n").Split('\n')
        .FirstOrDefault(line => line.StartsWith("## Interface:", StringComparison.Ordinal));

    private static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex(
        """^local generation = (?<generation>\d+)\nlocal fingerprint = "(?<fingerprint>[0-9a-f]{64})"\n(?<after>local bootstrap = "(?<bootstrap>[0-9a-f]{64})"\n.*)""",
        RegexOptions.Singleline)]
    private static partial Regex HeaderLine();

    [GeneratedRegex(
        """\{ name = "(?<name>(?:[^"\\]|\\.)*)", text = \[==\[.*?\]==\](?:, tag = "(?:[^"\\]|\\.)*")?, updatedAt = (?<updatedAt>\d+) \},\n""",
        RegexOptions.Singleline)]
    private static partial Regex GuideEntry();

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
