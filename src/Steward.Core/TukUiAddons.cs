namespace Steward.Core;

public static class TukUiAddons
{
    public const string Source = "TukUI";

    public static string Id(int projectId) => $"tukui-{projectId}";

    public static bool IsRecorded(ManagedAddon addon) =>
        addon?.Source == Source && addon.Id.StartsWith("tukui-", StringComparison.OrdinalIgnoreCase);

    public static ManagedAddon ToManagedAddon(ProviderAddonRecord record, string manifestBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ManagedAddon(record.Id, record.FolderName, manifestBaseUrl, Name: record.Name, Features: [StewardClient.AddonsFeature], Source: Source)
        {
            IconUrl = record.IconUrl,
            Folders = CurseForgeAddons.Folders(record),
            Website = record.WebsiteUrl,
            SourceLabel = Source,
            ManifestAuth = "none",
            ZipRelative = true,
            CurseForge = false,
        };
    }

    public static IReadOnlyList<ProviderAddonRecord> Identify(string addOnsPath, IEnumerable<string> folders, int? clientInterface, Func<int, string> manifestBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(manifestBaseUrl);

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<ProviderAddonRecord>();
        foreach (var folder in folders)
        {
            try
            {
                if (claimed.Contains(folder)
                    || LocalAddons.ReadDeclaredIds(addOnsPath, folder, clientInterface) is not { TukUiProjectId: { } projectId } ids
                    || records.Any(record => record.ModId == projectId))
                {
                    continue;
                }

                IReadOnlyList<string> present = [.. (ids.TukUiProjectFolders ?? []).Prepend(folder)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(module => !claimed.Contains(module) && Directory.Exists(Path.Combine(addOnsPath, module)))];
                claimed.UnionWith(present);
                var name = LocalAddons.Read(addOnsPath, folder, clientInterface)?.Name ?? folder;
                var baseUrl = manifestBaseUrl(projectId);
                records.Add(new ProviderAddonRecord(Id(projectId), folder, name, Source, projectId, 0, present, IconUrl: $"{baseUrl}icon.png"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return records;
    }
}
