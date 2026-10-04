using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Steward.Core;

public enum StewardGuidesWriteOutcome
{
    Written,
    Skipped,
    ChangedOnDisk,
    NotInstalled,
}

public readonly record struct StewardGuidesWriteResult(StewardGuidesWriteOutcome Outcome, long Generation);

public static partial class StewardGuidesAddon
{
    public const string FolderName = "StewardGuides";
    public const string AddonId = "steward-guides";

    private const string Terminator = "]==]";
    private const string TitleLine = "## Title: Steward: Guides";

    public static string? Hash(string guide)
    {
        ArgumentNullException.ThrowIfNull(guide);

        return GuideHeader().Match(guide) is { Success: true } match ? match.Groups[1].Value : null;
    }

    public static string Render(IReadOnlyList<(string Name, string Text, string? Tag, long UpdatedAt)> guides, long generation)
    {
        ArgumentNullException.ThrowIfNull(guides);

        var after = new StringBuilder("local guides = {\n");
        foreach (var (name, text, tag, updatedAt) in guides)
        {
            if (text.Contains(Terminator, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{name} contains the long-bracket terminator {Terminator}");
            }

            after.Append("    { name = \"").Append(Quote(name)).Append("\", text = [==[").Append(text.Trim()).Append("]==]");
            if (tag is not null)
            {
                after.Append(", tag = \"").Append(Quote(tag)).Append('"');
            }

            after.Append(", updatedAt = ").Append(updatedAt.ToString(CultureInfo.InvariantCulture)).Append(" },\n");
        }

        after.Append("}\nlocal _, ns = ...\nns.generation = generation\nns.guides = guides\n");
        var afterText = after.ToString();

        return new StringBuilder("local generation = ")
            .Append(generation.ToString(CultureInfo.InvariantCulture))
            .Append("\nlocal fingerprint = \"")
            .Append(Sha256Hex(afterText))
            .Append("\"\n")
            .Append(afterText)
            .ToString()
            .ReplaceLineEndings("\n");
    }

    public static StewardGuidesWriteResult Write(
        string addOnsPath, IReadOnlyList<(string Name, string Text, string? Tag, long UpdatedAt)> guides, long generation, bool force = false)
    {
        var lua = Render(guides, generation);

        var folder = Path.GetFullPath(Path.Combine(addOnsPath, FolderName));
        if (folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("WTF", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write a path containing a WTF segment: {folder}");
        }

        var tocPath = Path.Combine(folder, $"{FolderName}.toc");
        if (!File.Exists(tocPath) || !File.ReadAllLines(tocPath).Contains(TitleLine, StringComparer.Ordinal))
        {
            return new StewardGuidesWriteResult(StewardGuidesWriteOutcome.NotInstalled, generation);
        }

        var guidesPath = Path.Combine(folder, "Guides.lua");
        if (!force && Inspect(guidesPath, guides) is { } existing)
        {
            return existing;
        }

        var tempPath = guidesPath + ".tmp";
        File.WriteAllBytes(tempPath, Encoding.UTF8.GetBytes(lua));
        File.Move(tempPath, guidesPath, overwrite: true);
        return new StewardGuidesWriteResult(StewardGuidesWriteOutcome.Written, generation);
    }

    private static StewardGuidesWriteResult? Inspect(
        string guidesPath, IReadOnlyList<(string Name, string Text, string? Tag, long UpdatedAt)> guides)
    {
        if (!File.Exists(guidesPath))
        {
            return null;
        }

        var header = HeaderLine().Match(File.ReadAllText(guidesPath));
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

        return EntriesMatch(after, guides) ? new StewardGuidesWriteResult(StewardGuidesWriteOutcome.Skipped, generation) : null;
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

    private static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex(
        """^local generation = (?<generation>\d+)\nlocal fingerprint = "(?<fingerprint>[0-9a-f]{64})"\n(?<after>local guides = \{\n.*)""",
        RegexOptions.Singleline)]
    private static partial Regex HeaderLine();

    [GeneratedRegex(
        """\{ name = "(?<name>(?:[^"\\]|\\.)*)", text = \[==\[.*?\]==\](?:, tag = "(?:[^"\\]|\\.)*")?, updatedAt = (?<updatedAt>\d+) \},\n""",
        RegexOptions.Singleline)]
    private static partial Regex GuideEntry();

    private static string Quote(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    [GeneratedRegex(@"^\s*\d+\|([^:]+):")]
    private static partial Regex GuideHeader();
}
