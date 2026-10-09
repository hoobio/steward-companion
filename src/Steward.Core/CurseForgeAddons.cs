namespace Steward.Core;

public static class CurseForgeAddons
{
    public const string Source = "CurseForge";

    public static string Id(int modId, int versionType) => $"curseforge-{modId}-{versionType}";

    public static bool IsRecorded(ManagedAddon addon) =>
        addon?.IsCurseForge == true && addon.Id.StartsWith("curseforge-", StringComparison.OrdinalIgnoreCase);

    public static ManagedAddon ToManagedAddon(ProviderAddonRecord record, string manifestBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ManagedAddon(record.Id, record.FolderName, manifestBaseUrl, Name: record.Name, Features: [StewardClient.AddonsFeature], Source: record.Source)
        {
            IconUrl = record.IconUrl,
            Folders = Folders(record),
            Website = record.WebsiteUrl,
        };
    }

    public static IReadOnlyList<string> Folders(ProviderAddonRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return [.. record.Folders.Prepend(record.FolderName).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static string PrimaryFolder(IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return folders.MaxBy(folder => folders.Count(other =>
            !string.Equals(other, folder, StringComparison.OrdinalIgnoreCase) && other.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))!;
    }

    public static ProviderAddonRecord WithPrimaryFolder(ProviderAddonRecord record, Func<string, bool> folderExists)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(folderExists);
        var primary = PrimaryFolder([.. Folders(record).Where(folder => string.Equals(folder, record.FolderName, StringComparison.OrdinalIgnoreCase) || folderExists(folder))]);
        return string.Equals(primary, record.FolderName, StringComparison.Ordinal) ? record : record with { FolderName = primary };
    }

    public static CurseForgeMatchRequest MatchRequest(string addOnsPath, IEnumerable<string> folders, int? clientInterface = null)
    {
        var declared = new List<CurseForgeDeclared>();
        var fingerprints = new List<CurseForgeFolderFingerprint>();
        foreach (var folder in folders)
        {
            try
            {
                if (LocalAddons.ReadDeclaredIds(addOnsPath, folder, clientInterface)?.CurseProjectId is { } modId and > 0)
                {
                    declared.Add(new CurseForgeDeclared(folder, modId));
                }

                fingerprints.Add(new CurseForgeFolderFingerprint(folder, CurseForgeFingerprint.Compute(Path.Combine(addOnsPath, folder))));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return new CurseForgeMatchRequest(declared, fingerprints);
    }

    public static IReadOnlyList<ProviderAddonRecord> Adopt(
        IReadOnlyList<CurseForgeMatch> matches,
        int versionType,
        Func<string, bool> folderExists,
        Func<string, string?> title)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(folderExists);
        ArgumentNullException.ThrowIfNull(title);

        var byFolder = new Dictionary<string, CurseForgeMatch>(StringComparer.OrdinalIgnoreCase);
        // Steward API answers a declared-ID match with no fileId; ordering it last lets it win over a fingerprint match for the same folder.
        foreach (var match in matches.OrderBy(match => match.FileId is null ? 1 : 0))
        {
            byFolder[match.Folder] = match;
        }

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var adopted = new List<ProviderAddonRecord>();
        foreach (var group in byFolder.Values.GroupBy(match => match.ModId))
        {
            IReadOnlyList<string> modules = [.. group.SelectMany(match => (match.Folders ?? []).Append(match.Folder)).Distinct(StringComparer.OrdinalIgnoreCase)];
            IReadOnlyList<string> present = [.. modules.Where(folder => folderExists(folder) && !claimed.Contains(folder))];
            if (present.Count == 0)
            {
                continue;
            }

            claimed.UnionWith(present);
            var primary = PrimaryFolder(present);
            var name = group.Select(match => match.Name).FirstOrDefault(name => !string.IsNullOrEmpty(name)) ?? title(primary) ?? primary;
            var websiteUrl = group.Select(match => match.WebsiteUrl).FirstOrDefault(url => !string.IsNullOrEmpty(url));
            adopted.Add(new ProviderAddonRecord(Id(group.Key, versionType), primary, name, Source, group.Key, versionType, modules, WebsiteUrl: websiteUrl));
        }

        return adopted;
    }

    public static string KeptLocalKey(string flavourPath, ProviderAddonRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return AppStateStore.Key(flavourPath, record.FolderName);
    }

    public static IReadOnlyList<ProviderAddonRecord> Adoptable(
        IReadOnlyList<ProviderAddonRecord> identified,
        IReadOnlyList<ProviderAddonRecord> recorded,
        IEnumerable<string> keptLocal,
        string flavourPath)
    {
        ArgumentNullException.ThrowIfNull(identified);
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(keptLocal);

        var kept = keptLocal.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. identified.Where(record =>
            !recorded.Any(existing => string.Equals(existing.Id, record.Id, StringComparison.OrdinalIgnoreCase))
            && !kept.Contains(KeptLocalKey(flavourPath, record)))];
    }

    public static ProviderAddonRecord? MatchFor(IReadOnlyList<ProviderAddonRecord> identified, LocalAddon addon)
    {
        ArgumentNullException.ThrowIfNull(identified);
        ArgumentNullException.ThrowIfNull(addon);

        var folders = addon.FoldedFolders.Prepend(addon.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return identified.FirstOrDefault(record => Folders(record).Any(folders.Contains));
    }
}
