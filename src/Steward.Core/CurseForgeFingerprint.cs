using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Steward.Core;

public static partial class CurseForgeFingerprint
{
    private const uint Multiplier = 1540483477;

    public static uint Compute(string folderPath)
    {
        var root = Path.GetFullPath(Path.TrimEndingDirectorySeparator(folderPath));
        var folderName = Path.GetFileName(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(Path.GetFullPath, path => path, StringComparer.OrdinalIgnoreCase);

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tocs = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root))
        {
            var name = Path.GetFileName(file);
            if (TocName().Match(name) is { Success: true } toc && string.Equals(toc.Groups[1].Value, folderName, StringComparison.OrdinalIgnoreCase))
            {
                tocs.Add(file);
            }
            else if (string.Equals(name, "Bindings.xml", StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(file);
            }
        }

        foreach (var toc in tocs)
        {
            AddWithIncludes(toc, files, matched);
        }

        var hashes = matched.Select(file => Hash(File.ReadAllBytes(file))).Order();
        return Hash(Encoding.ASCII.GetBytes(string.Concat(hashes.Select(hash => hash.ToString(CultureInfo.InvariantCulture)))));
    }

    private static void AddWithIncludes(string path, Dictionary<string, string> files, HashSet<string> matched)
    {
        if (!files.TryGetValue(path, out var real) || !matched.Add(real))
        {
            return;
        }

        var extension = Path.GetExtension(real);
        var (comments, include) = extension switch
        {
            ".toc" => (TocComments(), TocInclude()),
            ".xml" => (XmlComments(), XmlInclude()),
            _ => (null, null),
        };
        if (comments is null || include is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(real)!;
        foreach (var line in comments.Replace(File.ReadAllText(real), "").Split('\n'))
        {
            if (include.Match(line.Trim()) is not { Success: true } match)
            {
                continue;
            }

            var target = match.Groups[1].Value.Trim();
            // WowUp stops reading a file's includes at the first one holding a control character or a pipe.
            if (target.Any(c => c < 0x20 || c == '|'))
            {
                break;
            }

            AddWithIncludes(Path.GetFullPath(Path.Combine(directory, target)), files, matched);
        }
    }

    public static uint Hash(ReadOnlySpan<byte> data)
    {
        var length = 0u;
        foreach (var b in data)
        {
            if (!IsWhitespace(b))
            {
                length++;
            }
        }

        var hash = 1u ^ length;
        var block = 0u;
        var shift = 0;
        foreach (var b in data)
        {
            if (IsWhitespace(b))
            {
                continue;
            }

            block |= (uint)b << shift;
            shift += 8;
            if (shift == 32)
            {
                var k = block * Multiplier;
                hash = (hash * Multiplier) ^ ((k ^ (k >> 24)) * Multiplier);
                block = 0;
                shift = 0;
            }
        }

        if (shift > 0)
        {
            hash = (hash ^ block) * Multiplier;
        }

        hash = (hash ^ (hash >> 13)) * Multiplier;
        return hash ^ (hash >> 15);
    }

    private static bool IsWhitespace(byte b) => b is 9 or 10 or 13 or 32;

    // WowUp's list plus forever: CurseForge's QuestieDB fingerprint (only flavour TOCs, one _Forever) matched only with it, verified 29 Sep 2026.
    [GeneratedRegex(@"^(.+?)([-|_](mainline|bcc|tbc|classic|vanilla|wrath|wotlkc|cata|mists|forever))?\.toc$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TocName();

    [GeneratedRegex(@"\s*#[^\n\r  ]*")]
    private static partial Regex TocComments();

    [GeneratedRegex(@"^\s*((?:(?<!\.\.).)+\.(?:xml|lua))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TocInclude();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComments();

    [GeneratedRegex(@"<(?:Include|Script)\s+file=[""']((?:(?<!\.\.).)+)[""']\s*/>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex XmlInclude();
}
