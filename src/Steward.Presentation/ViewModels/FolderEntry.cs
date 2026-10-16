using Steward.Core;

namespace Steward.App.ViewModels;

public sealed record FolderEntry(string AddOnsPath, string Folder, int? ClientInterface, string? ChangelogTitle = null, IReadOnlyList<ChangelogBlock>? Changelog = null, AddonNotice? Notice = null, string? Channel = null, string? Released = null, string? ReleasedTip = null)
{
    public static string Summary(int count) => count > 0 ? $"+ {count} folder{(count == 1 ? "" : "s")}" : "";

    public string NoticeGlyph => Glyphs.Notice(Notice);
}
