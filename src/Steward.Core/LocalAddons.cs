using System.Text.RegularExpressions;

using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Steward.Core;

public sealed record LocalAddon(string FolderName, string Name, string? Version, string? Interface, IReadOnlyList<string> FoldedFolders);

public sealed record DeclaredAddonIds(int? CurseProjectId, string? WagoId, string? WowInterfaceId);

public static class LocalAddons
{
    private sealed record Candidate(string FolderName, string Name, string? Version, string? Interface, IReadOnlyList<string> Deps);

    public static IReadOnlyList<LocalAddon> Scan(string addOnsPath, IEnumerable<string> excludedFolders, ILogger? logger = null)
    {
        if (!Directory.Exists(addOnsPath))
        {
            return [];
        }

        var excluded = new HashSet<string>(excludedFolders, StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

        List<string> dirs;
        try
        {
            dirs = [.. Directory.EnumerateDirectories(addOnsPath)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.Warn(ex, $"Local addon scan skipped {addOnsPath}");
            return [];
        }

        foreach (var dir in dirs)
        {
            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
            if (excluded.Contains(folderName))
            {
                continue;
            }

            try
            {
                if (ReadCandidate(dir, folderName) is { } candidate)
                {
                    candidates[folderName] = candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.Warn(ex, $"Local addon scan skipped {dir}");
            }
        }

        var targets = candidates.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Deps.Where(candidates.ContainsKey).Select(dep => candidates[dep].FolderName).FirstOrDefault(),
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
            var (candidate, folded) = (candidates[entry.Key], entry.Value);
            return new LocalAddon(
                candidate.FolderName,
                candidate.Name,
                candidate.Version,
                candidate.Interface,
                folded.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList());
        });

        return results.OrderBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? TopLevelToc(string folderPath, string folderName)
    {
        var exact = Path.Combine(folderPath, folderName + ".toc");
        return File.Exists(exact)
            ? exact
            : Directory.EnumerateFiles(folderPath, folderName + "_*.toc")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault();
    }

    public static DeclaredAddonIds? ReadDeclaredIds(string addOnsPath, string folderName)
    {
        var folderPath = Path.Combine(addOnsPath, folderName);
        if (!Directory.Exists(folderPath) || TopLevelToc(folderPath, folderName) is not { } tocPath)
        {
            return null;
        }

        return new DeclaredAddonIds(
            int.TryParse(TocFile.ReadDirective(tocPath, "X-Curse-Project-ID"), out var curseId) ? curseId : null,
            TocFile.ReadDirective(tocPath, "X-Wago-ID"),
            TocFile.ReadDirective(tocPath, "X-WoWI-ID"));
    }

    private static Candidate? ReadCandidate(string folderPath, string folderName)
    {
        if (TopLevelToc(folderPath, folderName) is not { } tocPath)
        {
            return null;
        }

        return new Candidate(
            folderName,
            StripColourCodes(TocFile.ReadDirective(tocPath, "Title")) ?? folderName,
            TocFile.ReadDirective(tocPath, "Version"),
            TocFile.ReadDirective(tocPath, "Interface"),
            [.. SplitDeps(TocFile.ReadDirective(tocPath, "Dependencies")), .. SplitDeps(TocFile.ReadDirective(tocPath, "RequiredDeps"))]);
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
