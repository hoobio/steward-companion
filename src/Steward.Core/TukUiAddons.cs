namespace Steward.Core;

public static class TukUiAddons
{
    public const string Source = "TukUI";

    public static string Id(int projectId) => $"tukui-{projectId}";

    public static bool IsRecorded(ManagedAddon addon) =>
        addon?.Source == Source && addon.Id.StartsWith("tukui-", StringComparison.OrdinalIgnoreCase);

    public static int? ProjectId(ManagedAddon addon) =>
        IsRecorded(addon) && int.TryParse(addon.Id.AsSpan("tukui-".Length), out var projectId) ? projectId : null;

    public static ManagedAddon ToManagedAddon(ProviderAddonRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ManagedAddon(record.Id, record.FolderName, $"{TukUiSource.ListUri.AbsoluteUri}/", Name: record.Name, Features: [StewardClient.AddonsFeature], Source: Source)
        {
            IconUrl = IsStewardRoute(record.IconUrl) ? null : record.IconUrl,
            Folders = CurseForgeAddons.Folders(record),
            Website = record.WebsiteUrl,
            SourceLabel = Source,
            ManifestAuth = "none",
            ZipRelative = false,
            CurseForge = false,
        };
    }

    public static bool IsStewardRoute(string? url) => url?.Contains("/api/addons/tukui/", StringComparison.OrdinalIgnoreCase) == true;

    public static ProviderAddonRecord Record(TukUiAddon addon)
    {
        ArgumentNullException.ThrowIfNull(addon);
        IReadOnlyList<string> folders = addon.Directories is { Count: > 0 } listed ? listed : [addon.Name ?? Id(addon.Id)];
        var name = addon.Name ?? folders[0];
        return new ProviderAddonRecord(Id(addon.Id), CurseForgeAddons.PrimaryFolder(folders), name, Source, addon.Id, 0, folders, TukUiSource.Https(addon.LogoSquareUrl), TukUiSource.Https(addon.WebUrl));
    }

    public static ProviderAddonRecord WithListing(ProviderAddonRecord record, TukUiAddon? addon) =>
        addon is null ? record : record with
        {
            IconUrl = TukUiSource.Https(addon.LogoSquareUrl) ?? (IsStewardRoute(record.IconUrl) ? null : record.IconUrl),
            WebsiteUrl = TukUiSource.Https(addon.WebUrl) ?? record.WebsiteUrl,
        };

    public static IReadOnlyList<ProviderAddonRecord> Identify(string addOnsPath, IEnumerable<string> folders, int? clientInterface, Func<int, TukUiAddon?> listing)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(listing);

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
                records.Add(WithListing(new ProviderAddonRecord(Id(projectId), folder, name, Source, projectId, 0, present), listing(projectId)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return records;
    }
}
