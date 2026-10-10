using Steward.Core;

namespace Steward.App.ViewModels;

public sealed record FolderEntry(string AddOnsPath, string Folder, int? ClientInterface, string? ChangelogTitle = null, IReadOnlyList<ChangelogBlock>? Changelog = null, AddonNotice? Notice = null, string? Channel = null)
{
    public static string Summary(int count) => count > 0 ? $"+ {count} folder{(count == 1 ? "" : "s")}" : "";

    public string NoticeGlyph => string.Equals(Notice?.Icon, "lock", StringComparison.OrdinalIgnoreCase) ? "" : "";
}
