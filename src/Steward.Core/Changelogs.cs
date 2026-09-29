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
}
