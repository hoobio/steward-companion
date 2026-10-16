using Steward.Core;

namespace Steward.App.ViewModels;

public static class Glyphs
{
    public const string Info = "Info";
    public const string CheckMark = "CheckMark";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Lock = "Lock";
    public const string ChevronUp = "ChevronUp";
    public const string ChevronDown = "ChevronDown";

    public static string Notice(AddonNotice? notice) =>
        string.Equals(notice?.Icon, "lock", StringComparison.OrdinalIgnoreCase) ? Lock : Info;
}
