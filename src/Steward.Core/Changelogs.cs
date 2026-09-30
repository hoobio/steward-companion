namespace Steward.Core;

public static class Changelogs
{
    public static IReadOnlyList<ChangelogBlock> For(AddonRelease? release)
    {
        if (release?.Changelog is { Count: > 0 } structured)
        {
            return structured;
        }

        return [.. (release?.Notes ?? []).Select(line => new ChangelogBlock("item", 0, 0, [new ChangelogRun(line)]))];
    }

    public static IReadOnlyList<ChangelogBlock> Combine(AddonRelease? parent, IEnumerable<(string Name, AddonRelease? Release)> children) =>
    [
        .. For(parent),
        .. children.Select(child => (child.Name, child.Release, Blocks: For(child.Release)))
            .Where(child => child.Blocks.Count > 0)
            .SelectMany(child => child.Blocks.Prepend(new ChangelogBlock("heading", 2, 0, [new ChangelogRun($"{child.Name} {child.Release!.Version}")]))),
    ];
}
