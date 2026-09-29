using System.Globalization;
using System.Web;

namespace Steward.Core;

public sealed record CurseForgeLink(int ModId, long FileId);

public enum CurseForgeLinkState
{
    Install,
    NotBuilt,
    NotDistributable,
    Installed,
    Update,
}

public static class CurseForgeLinks
{
    public const string Scheme = "curseforge";

    public static CurseForgeLink? Parse(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || !string.Equals(parsed.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parsed.Host, "install", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var query = HttpUtility.ParseQueryString(parsed.Query);
        return int.TryParse(query["addonId"], NumberStyles.None, CultureInfo.InvariantCulture, out var modId) && modId > 0
            && long.TryParse(query["fileId"], NumberStyles.None, CultureInfo.InvariantCulture, out var fileId) && fileId > 0
                ? new CurseForgeLink(modId, fileId)
                : null;
    }

    public static string Channel(int? releaseType) => releaseType is 2 or 3 ? "pre-release" : "release";

    public static bool IsNewer(AddonRelease linked, string? installedVersion, DateTimeOffset? installedReleased, DateTimeOffset? installedAt)
    {
        ArgumentNullException.ThrowIfNull(linked);
        if (string.Equals(linked.Version, installedVersion, StringComparison.Ordinal))
        {
            return false;
        }

        // The installed file was released no later than it was installed, so a file released after that is newer whatever its version string.
        return (installedReleased ?? installedAt) is { } baseline && linked.Released > baseline;
    }

    public static (string Channel, AddonRelease Release)? Alternative(IReadOnlyDictionary<string, AddonRelease?> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);
        return AddonChannelStatus.Ordered.FirstOrDefault(channel => releases.GetValueOrDefault(channel) is not null) is { } found
            ? (found, releases[found]!)
            : null;
    }

    public static IReadOnlyList<CurseForgeLatestFile> Choices(IReadOnlyList<CurseForgeLatestFile>? latest, CurseForgeModFile linked, long linkedFileId)
    {
        ArgumentNullException.ThrowIfNull(linked);
        return latest is { Count: > 0 }
            ? latest
            : [new CurseForgeLatestFile(linkedFileId, linked.File.Version, linked.GameVersionTypeIds is [var type, ..] ? type : 0, "Linked file", linked.ReleaseType ?? 1)];
    }

    public static int Preselect(IReadOnlyList<CurseForgeLatestFile> choices, long linkedFileId)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return Math.Max(0, choices.ToList().FindIndex(choice => choice.FileId == linkedFileId));
    }

    public static CurseForgeLinkState State(CurseForgeModFile file, int? versionType, bool installed, bool newer)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (versionType is not { } type || file.GameVersionTypeIds?.Contains(type) != true)
        {
            return CurseForgeLinkState.NotBuilt;
        }

        if (installed && !newer)
        {
            return CurseForgeLinkState.Installed;
        }

        if (!file.AllowDistribution || !file.File.Distributable)
        {
            return CurseForgeLinkState.NotDistributable;
        }

        return installed ? CurseForgeLinkState.Update : CurseForgeLinkState.Install;
    }
}
