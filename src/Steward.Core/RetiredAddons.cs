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

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(ReplacedBy) ? DisplayName : $"{DisplayName} (now part of {ReplacedBy})";
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
        var declined = new HashSet<string>(state.DeclinedRetiredAddons ?? [], StringComparer.OrdinalIgnoreCase);
        var found = new List<RetiredFolder>();
        foreach (var install in installs)
        {
            var addOnsPath = AccessLoss.AddOnsPath(install.FlavourPath);
            foreach (var addon in retired)
            {
                var entry = new RetiredFolder(install.FlavourPath, addon);
                if (AddonUpdater.RemovalRefusal(addOnsPath, entry.Folder) is null
                    && !install.ManagedFolders.Contains(entry.Folder, StringComparer.OrdinalIgnoreCase)
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

    public static (IReadOnlyList<RetiredFolder> Removed, IReadOnlyList<RetiredFolder> Deferred) Remove(
        IReadOnlyList<RetiredFolder> found, Func<string, bool> blocked, Action<RetiredFolder, Exception>? failed = null)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(blocked);
        var removed = new List<RetiredFolder>();
        var deferred = new List<RetiredFolder>();
        foreach (var entry in found)
        {
            if (blocked(entry.FlavourPath))
            {
                deferred.Add(entry);
                continue;
            }

            try
            {
                lock (AddonUpdater.AddOnsWriteLock)
                {
                    AddonUpdater.Uninstall(AccessLoss.AddOnsPath(entry.FlavourPath), [entry.Folder]);
                }

                removed.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failed?.Invoke(entry, ex);
            }
        }

        return (removed, deferred);
    }

    public static string Prompt(IReadOnlyList<RetiredFolder> found)
    {
        ArgumentNullException.ThrowIfNull(found);
        var labels = found.Select(entry => entry.Addon.Label).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return labels.Count == 1
            ? $"This addon was retired: {labels[0]}. Uninstall it?"
            : $"These addons were retired: {string.Join(", ", labels)}. Uninstall them?";
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

    public static string Deferred(IReadOnlyList<RetiredFolder> deferred)
    {
        ArgumentNullException.ThrowIfNull(deferred);
        var names = string.Join(", ", deferred.Select(entry => entry.Addon.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
        return $"{names} will be uninstalled when World of Warcraft closes.";
    }
}
