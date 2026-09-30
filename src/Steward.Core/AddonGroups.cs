namespace Steward.Core;

public static class AddonGroups
{
    public static IReadOnlyDictionary<string, string> FoldedInto(IReadOnlyList<ManagedAddon> visible)
    {
        var parents = visible.Where(addon => addon.Parent is null)
            .ToDictionary(addon => addon.Id, addon => addon.Id, StringComparer.OrdinalIgnoreCase);
        return visible
            .Where(addon => addon.Parent is { } parent && parents.ContainsKey(parent))
            .ToDictionary(addon => addon.Id, addon => parents[addon.Parent!], StringComparer.OrdinalIgnoreCase);
    }

    public static string? StoredChannel(IReadOnlyDictionary<string, string> channels, ManagedAddon addon) =>
        addon.Parent is { } parent && channels.TryGetValue(parent, out var channel) ? channel : channels.GetValueOrDefault(addon.Id);

    public static bool NeedsUpdate(bool parentHasUpdate, bool parentInstalled, IEnumerable<(bool Installed, bool HasUpdate)> children) =>
        parentHasUpdate || children.Any(child => child.HasUpdate && (child.Installed || parentInstalled));
}
