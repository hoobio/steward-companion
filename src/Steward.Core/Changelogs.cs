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

    public static IReadOnlyList<ChangelogBlock> Combine(
        (AddonRelease? Release, bool Pending) parent, IReadOnlyList<(string Name, AddonRelease? Release, bool Pending)> children)
    {
        var mutesCurrent = parent.Pending || children.Any(child => child.Pending);
        IEnumerable<ChangelogBlock> Mark(IEnumerable<ChangelogBlock> blocks, bool pending) =>
            mutesCurrent && !pending ? blocks.Select(block => block with { Muted = true }) : blocks;

        return
        [
            .. Mark(For(parent.Release), parent.Pending),
            .. children.Select(child => (child.Name, child.Release, child.Pending, Blocks: For(child.Release)))
                .Where(child => child.Blocks.Count > 0)
                .SelectMany(child => Mark(
                    child.Blocks.Prepend(new ChangelogBlock("heading", 2, 0, [new ChangelogRun($"{child.Name} {child.Release!.Version}")])),
                    child.Pending)),
        ];
    }
}
