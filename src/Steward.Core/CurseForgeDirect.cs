using System.Globalization;
using System.Text.Json;

namespace Steward.Core;

public sealed record CurseForgePublicFile(long Id, string Version, string FileName, long Size, DateTimeOffset Created, int ReleaseType, IReadOnlyList<int> VersionTypes, bool Available);

public sealed record CurseForgePublicFiles(IReadOnlyList<CurseForgePublicFile> Items, bool Complete);

// curseforge.com's website API is unsupported and disallowed in robots.txt, so every parse failure falls back to Steward API.
public static class CurseForgeDirect
{
    private const string ApiBase = "https://www.curseforge.com/api/v1/mods/";
    private const string CdnFiles = "https://edge.forgecdn.net/files";
    private const string RequiredDependency = "RequiredDependency";
    private const int ReleaseType = 1;
    private static readonly int[] PreReleaseTypes = [2, 3];

    // FileStatus 4 Approved and 10 Released stand in for the missing isAvailable; only 4 was seen live (23 Oct 2026) and the docs give no status-to-availability mapping.
    private static readonly long[] AvailableStatuses = [4, 10];

    public static (int ModId, int VersionType)? Ids(string? manifestBaseUrl) =>
        Uri.TryCreate(manifestBaseUrl, UriKind.Absolute, out var uri)
        && uri.Segments is [.., "curseforge/", var mod, var type]
        && int.TryParse(mod.TrimEnd('/'), CultureInfo.InvariantCulture, out var modId) && modId > 0
        && int.TryParse(type.TrimEnd('/'), CultureInfo.InvariantCulture, out var versionType) && versionType > 0
            ? (modId, versionType)
            : null;

    public static Uri FilesUri(int modId) => new($"{ApiBase}{modId}/files?pageIndex=0&pageSize=50&sort=dateCreated&sortDescending=true");

    public static Uri DependenciesUri(int modId) => new($"{ApiBase}{modId}/dependencies?index=0&pageSize=50");

    public static string ZipUrl(long fileId, string fileName) => $"{CdnFiles}/{fileId / 1000}/{fileId % 1000}/{Uri.EscapeDataString(fileName)}";

    public static CurseForgePublicFiles ParseFiles(JsonElement root)
    {
        var data = Data(root);
        var listed = 0;
        var items = new List<CurseForgePublicFile>();
        foreach (var item in data)
        {
            listed++;
            if (ParseFile(item) is { } file)
            {
                items.Add(file);
            }
        }

        var total = root.TryGetProperty("pagination", out var pagination) && pagination.ValueKind == JsonValueKind.Object ? Number(pagination, "totalCount") : null;
        return new CurseForgePublicFiles(items, items.Count == listed && total is { } count && count <= listed);
    }

    public static IReadOnlyList<AddonRequirement> ParseRequires(JsonElement root) =>
        [.. Data(root)
            .Where(item => item.ValueKind == JsonValueKind.Object && Text(item, "type") == RequiredDependency && Number(item, "id") is > 0)
            .Select(item => new AddonRequirement($"curseforge:{Number(item, "id")}", Text(item, "name")))];

    public static CurseForgePublicFile? Select(IEnumerable<CurseForgePublicFile> files, string channel, int versionType) =>
        Candidates(files, channel, versionType).Where(file => file.Available).MaxBy(file => file.Created);

    public static bool HasUnavailable(IEnumerable<CurseForgePublicFile> files, string channel, int versionType) =>
        Candidates(files, channel, versionType).Any(file => !file.Available);

    private static IEnumerable<CurseForgePublicFile> Candidates(IEnumerable<CurseForgePublicFile> files, string channel, int versionType)
    {
        int[] releaseTypes = channel switch
        {
            "release" => [ReleaseType],
            "pre-release" => PreReleaseTypes,
            _ => [],
        };
        return files.Where(file => releaseTypes.Contains(file.ReleaseType) && file.VersionTypes.Contains(versionType));
    }

    public static AddonRelease ToRelease(CurseForgePublicFile file, string? website)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new AddonRelease(file.Version, ZipUrl(file.Id, file.FileName), null, file.Size, file.Created, Website: website) { DirectFileId = file.Id };
    }

    private static JsonElement.ArrayEnumerator Data(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray()
            : throw new JsonException("curseforge.com answered without a data[] array");

    private static CurseForgePublicFile? ParseFile(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || Number(item, "id") is not (> 0 and var id)
            || Text(item, "fileName") is not { Length: > 0 } fileName
            || fileName is "." or ".."
            || fileName.Any(character => character is '/' or '\\' || char.IsControl(character))
            || !DateTimeOffset.TryParse(Text(item, "dateCreated"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created)
            || !item.TryGetProperty("gameVersionTypeIds", out var versionTypes) || versionTypes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return new CurseForgePublicFile(
            id,
            Text(item, "displayName") is { Length: > 0 } displayName ? displayName : fileName,
            fileName,
            Number(item, "fileLength") ?? 0,
            created,
            (int)(Number(item, "releaseType") ?? 0),
            [.. versionTypes.EnumerateArray().Where(type => type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out _)).Select(type => type.GetInt32())],
            Number(item, "status") is { } status && AvailableStatuses.Contains(status));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
}
