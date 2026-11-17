using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed record RetiredAddon(
    [property: JsonPropertyName("folder_name")] string? FolderName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("replaced_by")] string? ReplacedBy = null)
{
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FolderName ?? string.Empty : Name;
}

public sealed record RetiredFolder(string FlavourPath, RetiredAddon Addon)
{
    public string Folder => Addon.FolderName!;
}

public sealed record RetiredInstall(string FlavourPath, IReadOnlyCollection<string> ManagedFolders);

public static class RetiredAddons
{
    public static List<RetiredAddon>? Listed(IReadOnlyList<RetiredAddon>? retired) =>
        retired?.Where(addon => addon is not null && !string.IsNullOrWhiteSpace(addon.FolderName)).ToList();

    public static RetiredAddon? For(IReadOnlyList<RetiredAddon>? retired, IEnumerable<string> folders) =>
        Listed(retired)?.FirstOrDefault(addon => folders.Contains(addon.FolderName, StringComparer.OrdinalIgnoreCase));

    public static string DeclineKey(RetiredFolder found)
    {
        ArgumentNullException.ThrowIfNull(found);
        return AppStateStore.Key(found.FlavourPath, found.Folder);
    }

    public static IReadOnlyList<RetiredFolder> Find(AppState state, IEnumerable<RetiredInstall> installs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(installs);
        var retired = Listed(state.RetiredAddons) ?? [];
        if (state.AddonCatalogue is not { Count: > 0 } catalogue)
        {
            return [];
        }

        var declined = new HashSet<string>(state.DeclinedRetiredAddons ?? [], StringComparer.OrdinalIgnoreCase);
        var found = new List<RetiredFolder>();
        foreach (var install in installs)
        {
            var addOnsPath = AccessLoss.AddOnsPath(install.FlavourPath);
            var inUse = new HashSet<string>(install.ManagedFolders, StringComparer.OrdinalIgnoreCase);
            inUse.UnionWith(catalogue.Select(addon => addon.FolderName));
            inUse.UnionWith(state.Installs
                .Where(record => record.Key.StartsWith(AppStateStore.Key(install.FlavourPath, string.Empty), StringComparison.OrdinalIgnoreCase))
                .SelectMany(record => record.Value.Folders ?? []));
            inUse.UnionWith((state.ProviderAddons?.GetValueOrDefault(install.FlavourPath) ?? []).SelectMany(CurseForgeAddons.Folders));
            foreach (var addon in retired)
            {
                var entry = new RetiredFolder(install.FlavourPath, addon);
                if (AddonUpdater.RemovalRefusal(addOnsPath, entry.Folder) is null
                    && !inUse.Contains(entry.Folder)
                    && !declined.Contains(DeclineKey(entry))
                    && !(state.LostAccess ?? []).Values.Any(lost => string.Equals(lost.FlavourPath, install.FlavourPath, StringComparison.OrdinalIgnoreCase)
                        && lost.Folders.Contains(entry.Folder, StringComparer.OrdinalIgnoreCase))
                    && Directory.Exists(Path.Combine(addOnsPath, entry.Folder))
                    && !AddonUpdater.IsLink(Path.Combine(addOnsPath, entry.Folder))
                    && !found.Any(other => string.Equals(other.FlavourPath, entry.FlavourPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(other.Folder, entry.Folder, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(entry);
                }
            }
        }

        return found;
    }

    public static Task<(IReadOnlyList<RetiredFolder> Removed, IReadOnlyList<RetiredFolder> Deferred)> RemoveAsync(
        IReadOnlyList<RetiredFolder> found, Func<string, bool> blocked, Action<RetiredFolder, Exception>? failed = null, Func<string, string, Task>? recycle = null)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(blocked);
        return Task.Run<(IReadOnlyList<RetiredFolder>, IReadOnlyList<RetiredFolder>)>(() =>
        {
            var removed = new List<RetiredFolder>();
            var deferred = new List<RetiredFolder>();
            foreach (var entry in found)
            {
                try
                {
                    lock (AddonUpdater.AddOnsWriteLock)
                    {
                        if (blocked(entry.FlavourPath))
                        {
                            deferred.Add(entry);
                            continue;
                        }

                        (recycle ?? AddonRecycler.RecycleAsync)(AccessLoss.AddOnsPath(entry.FlavourPath), entry.Folder).GetAwaiter().GetResult();
                    }

                    removed.Add(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    failed?.Invoke(entry, ex);
                }
            }

            return (removed, deferred);
        });
    }

    public static List<string> PruneDeclines(List<string> declines, IReadOnlyList<RetiredAddon>? listed) =>
        listed is null
            ? declines
            : [.. declines.Where(key => listed.Any(addon => string.Equals(addon.FolderName, key[(key.LastIndexOf('|') + 1)..], StringComparison.OrdinalIgnoreCase)))];

    public static string Prompt(IReadOnlyList<RetiredFolder> found, Func<string, string> flavourLabel)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(flavourLabel);
        var labels = found.Select(entry => string.IsNullOrWhiteSpace(entry.Addon.ReplacedBy)
            ? $"{entry.Addon.DisplayName} (folder {entry.Folder}, {flavourLabel(entry.FlavourPath)})"
            : $"{entry.Addon.DisplayName} (folder {entry.Folder}, {flavourLabel(entry.FlavourPath)}, now part of {entry.Addon.ReplacedBy})").ToList();
        return labels.Count == 1
            ? $"This addon was retired: {labels[0]}. Uninstall it? Its folder is moved to {AddonRecycler.Destination}."
            : $"These addons were retired: {string.Join(", ", labels)}. Uninstall them? Their folders are moved to {AddonRecycler.Destination}.";
    }

    public static string? Reasons(IReadOnlyList<RetiredFolder> found)
    {
        ArgumentNullException.ThrowIfNull(found);
        var reasons = found.Select(entry => entry.Addon)
            .Where(addon => !string.IsNullOrWhiteSpace(addon.Reason))
            .Select(addon => $"{addon.DisplayName}: {addon.Reason!.Trim().TrimEnd('.')}.")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return reasons.Count == 0 ? null : string.Join(" ", reasons);
    }

    public static string Failed(RetiredFolder entry, string installLabel, Exception error)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(error);
        return $"{entry.Addon.DisplayName} (folder {entry.Folder}, {installLabel}) was not uninstalled and is still in the AddOns folder: {error.Message}";
    }

    public static string Deferred(IReadOnlyList<RetiredFolder> deferred)
    {
        ArgumentNullException.ThrowIfNull(deferred);
        var names = string.Join(", ", deferred.Select(entry => entry.Addon.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
        return $"{names} will be uninstalled when World of Warcraft closes.";
    }
}
