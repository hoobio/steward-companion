using Steward.App.ViewModels;

namespace Steward.App.Services;

public static class ScreenshotPreview
{
    public const string Argument = "--screenshot";

    public const string UserName = "Adventurer";

    public const string GuildName = "Steward Guild";

    public const string HiddenAddonId = "hoobiscripts";

    public static bool IsRequested(IEnumerable<string> arguments) =>
        arguments.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    public static void Apply(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        main.IsScreenshot = true;
        GuidesPreview.ApplyGuides(main, "current");
    }
}
