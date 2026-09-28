namespace Steward.Core;

public static class CurseForgeAddons
{
    public const string Source = "CurseForge";

    public static string Id(int modId, int versionType) => $"curseforge-{modId}-{versionType}";

    public static ManagedAddon ToManagedAddon(ProviderAddonRecord record, string manifestBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ManagedAddon(record.Id, record.FolderName, manifestBaseUrl, Name: record.Name, Features: [GigagrugClient.CurseForgeFeature], Source: record.Source)
        {
            IconUrl = record.IconUrl,
            Folders = Folders(record),
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
        return folders.FirstOrDefault(folder => folders.All(other => other.StartsWith(folder, StringComparison.OrdinalIgnoreCase))) ?? folders[0];
    }

    public static CurseForgeMatchRequest MatchRequest(string addOnsPath, IEnumerable<string> folders)
    {
        var declared = new List<CurseForgeDeclared>();
        var fingerprints = new List<CurseForgeFolderFingerprint>();
        foreach (var folder in folders)
        {
            try
            {
                if (LocalAddons.ReadDeclaredIds(addOnsPath, folder)?.CurseProjectId is { } modId and > 0)
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
        // gigagrug answers a declared-ID match with no fileId; ordering it last lets it win over a fingerprint match for the same folder.
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
            adopted.Add(new ProviderAddonRecord(Id(group.Key, versionType), primary, title(primary) ?? primary, Source, group.Key, versionType, modules));
        }

        return adopted;
    }
}
