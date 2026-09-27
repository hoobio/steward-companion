using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.App.Services;

public static class BannersPreview
{
    public const string Argument = "--banners-preview";

    public static bool IsRequested(IEnumerable<string> arguments) =>
        arguments.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    public static void Apply(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        main.UserName = "Hoobi";
        main.Role = "global";
        main.IsAuthorized = true;
        main.IsSignedIn = true;
        main.ApplyBannersPreview(SampleBanners());
        // A local, network-free scan, so a real install on this machine shows the page in its normal (not empty) state too.
        main.RescanCommand.Execute(null);
    }

    private static IReadOnlyList<Banner> SampleBanners() =>
    [
        new Banner(
            "preview-info", 1, "info", "New in this release", "You can now hide a banner once you have read it.",
            Actions: [new BannerAction("Hide", "dismiss")]),
        new Banner(
            "preview-success", 1, "success", "Sync is healthy", "Character sync completed without errors.",
            Actions: [new BannerAction("Details", "open_url", Url: "https://guild.hoobi.io")]),
        new Banner(
            "preview-warning", 1, "warning", "Addon manifest slow to respond", "Update checks may lag a few minutes.",
            Actions: [new BannerAction("Addons", "navigate", Page: "addons")]),
        new Banner(
            "preview-error", 3, "error", "Update required", "You must update your client to 0.13.0 or newer",
            Dismissible: false,
            Actions:
            [
                new BannerAction("Update", "store_update"),
                new BannerAction("Details", "open_url", Url: "https://github.com/hoobio/steward-companion/releases"),
                new BannerAction("Sync", "navigate", Page: "sync"),
                new BannerAction("Hide", "dismiss"),
            ]),
    ];
}
