using System.Reflection;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class HomePageRenderTests(AvaloniaSession session)
{
    private const string ClientVersion = "1.16.1.70000";
    [Fact]
    public void Home_page_renders_the_empty_state_the_table_and_the_bars() => session.Run(() =>
    {
        var temp = session.NewFolder();
        var platform = new AvaloniaPlatform();
        using var host = session.NewHost(platform);
        platform.Http = null;
        var main = host.Services.GetRequiredService<MainViewModel>();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Width = 1280;
        window.Height = 820;
        window.Show();

        main.UserName = "Hoobi";
        main.IsSignedIn = true;
        main.IsAuthorized = true;
        Assert.IsType<HomePage>(window.CurrentPage);
        Assert.True(Save(window, "home-no-installs").NoInstallsVisibility);

        var install = Seed(main, host.Services.GetRequiredService<AppStateStore>(), temp);
        Pump();
        var page = (HomePage)window.CurrentPage!;
        var rows = page.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("TableRow"));
        Assert.Equal(main.TableRows.Count, rows);
        Assert.Equal(5, main.TableRows.Count);
        Save(window, "home-table");

        var more = page.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "More options");
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        menu.ShowAt(more);
        Pump();
        Assert.IsType<AddonRowViewModel>(menu.Items.OfType<MenuItem>().First().DataContext);
        menu.Hide();

        main.FilterIndex = 2;
        Pump();
        Assert.Equal(5, main.TableRows.Count);
        Save(window, "home-hidden");
        main.FilterIndex = 0;

        main.StatusMessage = "Steward APIs are temporarily unavailable.";
        main.AppUpdate = new AddonRelease("0.24.0", null, null, 0, DateTimeOffset.Now);
        main.ApplyBannersPreview(
        [
            new Banner("info", 1, "info", "Install World of Warcraft: Forever", "The live client is now in the Battle.net app as \"Stress Test\". Install it to start managing your addons.",
                Actions: [new BannerAction("Open Battle.net", "open_url", Url: "https://example.invalid")]),
            new Banner("error", 1, "error", "Update required", "", Dismissible: false),
        ]);
        Pump();
        Save(window, "home-bars");

        window.Hide();
        main.Installs.Remove(install);
    });

    internal static WowInstallViewModel Seed(MainViewModel main, AppStateStore store, string temp)
    {
        var flavour = Path.Combine(temp, "_forever_");
        var addOns = Path.Combine(flavour, "Interface", "AddOns");
        var current = TocFile.InterfaceNumber(ClientVersion)!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Toc(addOns, "Steward", "Steward", "0.0.0-d3f1d44", current);
        Toc(addOns, "HoobiScripts", "Hoobi Scripts", "0.0.0-ec7e7a3", current);
        Toc(addOns, "RXPAuthor", "RXP Author", "0.0.0-7800414", current);
        Toc(addOns, "RXPGuides", "RestedXP Guides", "v4.11.22", current);
        Toc(addOns, "Details", "Details! Damage Meter", "Details.20251004.13450.162", current);
        Toc(addOns, "WeakAuras", "WeakAuras", "5.19.12", "11507");
        foreach (var module in new[] { "Steward_Roster", "Steward_Loot", "Steward_Attendance" })
        {
            Toc(addOns, module, module, "0.0.0-d3f1d44", current);
        }

        var catalogue = new List<CatalogueAddon>
        {
            new("steward", "Steward", "https://example.invalid/steward/", "Steward"),
            new("hoobiscripts", "HoobiScripts", "https://example.invalid/hoobiscripts/", "Hoobi Scripts", "Protected", Notice: new AddonNotice("lock", "Shared copies identify who fetched them")),
            new("rxpauthor", "RXPAuthor", "https://example.invalid/rxpauthor/", "RXP Author"),
            new("restedxp", "RXPGuides", "https://example.invalid/restedxp/", "RestedXP Guides", "GitHub"),
            new("questie", "Questie", "https://example.invalid/questie/", "Questie"),
        };
        typeof(MainViewModel).GetMethod("SetAddonCatalogue", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [catalogue]);

        var installs = new Dictionary<string, InstalledAddonRecord>(StringComparer.OrdinalIgnoreCase)
        {
            [AppStateStore.Key(flavour, "steward")] = new("0.0.0-d3f1d44", "pre-release", null, DateTimeOffset.Now.AddDays(-1), Folders: ["Steward", "Steward_Roster", "Steward_Loot", "Steward_Attendance"]),
        };
        store.Save(store.Load() with { Installs = installs });

        var wow = new WowInstall(temp, "_forever_", flavour, addOns, "wowf", ClientVersion, "World of Warcraft: Forever");
        var install = (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [wow, false])!;
        var rescan = install.RescanLocalAsync();
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return rescan.IsCompletedSuccessfully;
            },
            TimeSpan.FromSeconds(10)));

        var notes = new[] { "Fixed the roster export", "Faster startup" };
        install.ApplyStatus(
            new Dictionary<string, AddonChannelStatus>
            {
                ["steward"] = Status("pre-release", Release("release", "0.0.0-a1b2c3d", 3, notes), Release("pre-release", "0.0.0-d3f1d44", 1, notes)),
                ["hoobiscripts"] = Status("pre-release", null, Release("pre-release", "0.0.0-ec7e7a3", 0, notes)),
                ["rxpauthor"] = Status("pre-release", null, Release("pre-release", "0.0.0-7800414", 1, notes)),
                ["restedxp"] = Status("release", Release("release", "v4.11.23", 4, notes), null),
                ["questie"] = Status("release", Release("release", "11.2.3", 2, null), null),
            },
            background: false);
        main.SelectedInstall = install;
        return install;
    }

    private static AddonChannelStatus Status(string channel, AddonRelease? release, AddonRelease? preRelease) =>
        new(new Dictionary<string, AddonRelease?> { ["release"] = release, ["pre-release"] = preRelease }, channel, null);

    private static AddonRelease Release(string channel, string version, int daysAgo, IReadOnlyList<string>? notes) =>
        new(version, $"{channel}.zip", null, 0, DateTimeOffset.Now.AddDays(-daysAgo).AddMinutes(-5), notes);

    private static void Toc(string addOns, string folder, string title, string version, string interfaceNumber)
    {
        Directory.CreateDirectory(Path.Combine(addOns, folder));
        File.WriteAllText(Path.Combine(addOns, folder, $"{folder}.toc"), $"## Interface: {interfaceNumber}\n## Title: {title}\n## Version: {version}\n");
    }

    private static void Pump()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static MainViewModel Save(MainWindow window, string name)
    {
        Pump();
        var frame = window.CaptureRenderedFrame();
        Pump();
        frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("STEWARD_HOME_PNG_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        TextCentring.Verify(window, name);
        return window.ViewModel;
    }
}
