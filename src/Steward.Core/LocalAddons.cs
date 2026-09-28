using System.Text.RegularExpressions;

namespace Steward.Core;

public sealed record LocalAddon(string FolderName, string Name, string? Version, string? Interface, IReadOnlyList<string> FoldedFolders);

public static class LocalAddons
{
    public static IReadOnlyList<LocalAddon> Scan(string addOnsPath, IEnumerable<string> excludedFolders)
    {
        if (!Directory.Exists(addOnsPath))
        {
            return [];
        }

        var excluded = new HashSet<string>(excludedFolders, StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.EnumerateDirectories(addOnsPath))
        {
            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
            if (excluded.Contains(folderName))
            {
                continue;
            }

            if (FindOwnToc(dir, folderName) is { } tocPath)
            {
                candidates[folderName] = tocPath;
            }
        }

        var targets = candidates.ToDictionary(
            entry => entry.Key,
            entry => ImmediateTarget(entry.Value, candidates.Keys),
            StringComparer.OrdinalIgnoreCase);

        var roots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var folderName in candidates.Keys)
        {
            var root = ResolveRoot(folderName, targets);
            if (!roots.TryGetValue(root, out var members))
            {
                members = [];
                roots[root] = members;
            }

            if (!string.Equals(root, folderName, StringComparison.OrdinalIgnoreCase))
            {
                members.Add(folderName);
            }
        }

        var results = roots.Select(entry =>
        {
            var (folderName, folded) = (entry.Key, entry.Value);
            var tocPath = candidates[folderName];
            var name = StripColourCodes(TocFile.ReadDirective(tocPath, "Title")) ?? folderName;
            return new LocalAddon(
                folderName,
                name,
                TocFile.ReadDirective(tocPath, "Version"),
                TocFile.ReadDirective(tocPath, "Interface"),
                folded.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList());
        });

        return results.OrderBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? FindOwnToc(string folderPath, string folderName)
    {
        var exact = Path.Combine(folderPath, folderName + ".toc");
        if (File.Exists(exact))
        {
            return exact;
        }

        return Directory.EnumerateFiles(folderPath, folderName + "_*.toc")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string? ImmediateTarget(string tocPath, IEnumerable<string> candidateNames)
    {
        var deps = SplitDeps(TocFile.ReadDirective(tocPath, "Dependencies"))
            .Concat(SplitDeps(TocFile.ReadDirective(tocPath, "RequiredDeps")));

        foreach (var dep in deps)
        {
            var match = candidateNames.FirstOrDefault(name => string.Equals(name, dep, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitDeps(string? value) =>
        value is null ? [] : value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0);

    private static string ResolveRoot(string name, Dictionary<string, string?> targets)
    {
        var path = new List<string>();
        var current = name;
        while (true)
        {
            if (path.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                return name;
            }

            path.Add(current);
            if (!targets.TryGetValue(current, out var next) || next is null)
            {
                return current;
            }

            current = next;
        }
    }

    private static string? StripColourCodes(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var stripped = Regex.Replace(value, @"\|c[0-9A-Fa-f]{8}", "").Replace("|r", "");
        return stripped.Length == 0 ? null : stripped;
    }
}
