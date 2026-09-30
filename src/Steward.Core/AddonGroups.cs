namespace Steward.Core;

public static class AddonGroups
{
    public static IReadOnlyDictionary<string, string> FoldedInto(IReadOnlyList<ManagedAddon> visible)
    {
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addon in visible.Where(addon => addon.Parent is null))
        {
            parents.TryAdd(addon.Id, addon.Id);
        }

        var folded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addon in visible)
        {
            if (addon.Parent is { } parent && parents.TryGetValue(parent, out var parentId))
            {
                folded.TryAdd(addon.Id, parentId);
            }
        }

        return folded;
    }

    public static bool IsRolledUp(bool distributable, bool hidden, bool ignored) => distributable && !hidden && !ignored;

    public static string? StoredChannel(IReadOnlyDictionary<string, string> channels, ManagedAddon addon) =>
        addon.Parent is { } parent && channels.TryGetValue(parent, out var channel) ? channel : channels.GetValueOrDefault(addon.Id);

    public static bool NeedsUpdate(bool parentHasUpdate, bool parentInstalled, IEnumerable<(bool Installed, bool HasUpdate)> children) =>
        parentHasUpdate || children.Any(child => child.HasUpdate && (child.Installed || parentInstalled));
}
