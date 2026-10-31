using System.Reflection;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;
using Steward.Desktop.Views;

namespace Steward.Desktop.Tests;

public sealed class ClientFlavourTests(AvaloniaSession session)
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void A_non_Forever_install_gets_only_RestedXP_and_local_rows_and_hides_Sync() => session.Run(() =>
    {
        var temp = session.NewFolder();
        var platform = new AvaloniaPlatform();
        using var host = session.NewHost(platform);
        platform.Http = null;
        var main = host.Services.GetRequiredService<MainViewModel>();
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        main.UserName = "Hoobi";
        main.IsSignedIn = true;
        main.IsAuthorized = true;
        ((HashSet<string>)typeof(MainViewModel).GetField("_features", Private)!.GetValue(main)!).Add(StewardClient.AddonsFeature);
        ((HashSet<string>)typeof(MainViewModel).GetField("_guildFeatures", Private)!.GetValue(main)!).Add(StewardClient.StewardFeature);
        typeof(MainViewModel).GetMethod("SetAddonCatalogue", Private)!.Invoke(main, [new List<CatalogueAddon>
        {
            new("steward", "Steward", "https://example.invalid/steward/", "Steward"),
            new("hoobiscripts", "HoobiScripts", "https://example.invalid/hoobiscripts/", "Hoobi Scripts", "Protected"),
            new("restedxp", "RXPGuides", "https://example.invalid/restedxp/", "RestedXP Guides", "GitHub", Products: ["*"]),
            new("bugsack", "BugSack", "https://example.invalid/bugsack/", "BugSack", "CurseForge"),
        }]);

        var forever = AddInstall(main, temp, "_forever_", "wowf", "1.60.1.70245", "World of Warcraft: Forever");
        var era = AddInstall(main, temp, "_classic_era_", "wow_classic_era", "1.15.9.64000", "World of Warcraft: Classic Era", "BugSack", "Steward");
        var rescan = era.RescanLocalAsync();
        UiPaths.UiPathWorld.WaitFor(() => rescan.IsCompleted);

        Assert.Equal(["bugsack", "hoobiscripts", "restedxp", "steward"], forever.AddonRows.Select(row => row.AddonId).Order());
        Assert.Equal(["restedxp"], era.AddonRows.Select(row => row.AddonId));
        Assert.Contains(era.LocalRows, row => row.FolderName == "BugSack");

        main.SelectedInstall = forever;
        Assert.True(main.SyncVisibility);
        window.FindControl<Button>("SyncItem")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<SyncPage>(window.CurrentPage);

        main.SelectedInstall = era;
        Dispatcher.UIThread.RunJobs();
        Assert.False(main.SyncVisibility);
        Assert.IsType<HomePage>(window.CurrentPage);
        main.ApplyProducts([new("wowf", Events: [new("Launch", DateTimeOffset.Now.AddDays(3).ToUnixTimeMilliseconds(), Headline: true)])]);
        Assert.False(main.CountdownVisibility);
        main.SelectedInstall = forever;
        Assert.True(main.CountdownVisibility);
        Assert.Equal("2 days 23 hours", main.LaunchCountdown);
        main.ApplyProducts([new("wowf", Events: []), new("wow_classic_era", Events: [new("Patch", DateTimeOffset.Now.AddDays(1).ToUnixTimeMilliseconds(), Headline: true)])]);
        Assert.False(main.CountdownVisibility);
        Assert.False(main.ScheduleVisibility);
        main.SelectedInstall = era;
        Assert.True(main.CountdownVisibility);
        Assert.Equal("World of Warcraft: Classic Era", main.ScheduleTitle);
        main.ApplyProducts([]);

        window.Hide();
        main.Installs.Remove(forever);
        main.Installs.Remove(era);
    });

    [Theory]
    [InlineData("wowf", "Forever Leveling Guide - Both Factions", true)]
    [InlineData("wow", "Forever Leveling Guide - Both Factions", false)]
    [InlineData("wow_classic", "Mists of Pandaria Leveling Guide", true)]
    [InlineData("wow", "Mists of Pandaria Leveling Guide", false)]
    [InlineData("wow", "The War Within Leveling Guide", true)]
    [InlineData("wow_classic_era", "The War Within Leveling Guide", false)]
    [InlineData("wow_anniversary", "The Burning Crusade Leveling Guide", true)]
    [InlineData("wow_classic_era", "Hardcore Leveling Guide", true)]
    [InlineData("wow_classic_ptr", "Mists of Pandaria Leveling Guide", false)]
    public void A_guide_goes_to_the_client_its_name_prefix_names_and_retail_takes_the_rest(string product, string guide, bool allowed)
    {
        var prefixes = new Dictionary<string, string[]>
        {
            ["wowf"] = ["Forever"],
            ["wow_classic"] = ["Mists of Pandaria"],
            ["wow_classic_era"] = ["Classic Era", "Season of Mastery", "Hardcore"],
            ["wow_anniversary"] = ["The Burning Crusade"],
            ["wow"] = [""],
        };

        Assert.Equal(allowed, Steward.App.Services.RestedXpService.Allows(prefixes, product, guide));
    }

    private static WowInstallViewModel AddInstall(MainViewModel main, string temp, string flavour, string product, string client, string name, params string[] folders)
    {
        var path = Path.Combine(temp, flavour);
        var addOns = Path.Combine(path, "Interface", "AddOns");
        Directory.CreateDirectory(addOns);
        var interfaceNumber = TocFile.InterfaceNumber(client)!.Value;
        foreach (var folder in folders)
        {
            Directory.CreateDirectory(Path.Combine(addOns, folder));
            File.WriteAllText(Path.Combine(addOns, folder, $"{folder}.toc"), $"## Interface: {interfaceNumber}\n## Title: {folder}\n## Version: 1.0.0\n");
        }

        return (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", Private)!
            .Invoke(main, [new WowInstall(temp, flavour, path, addOns, product, client, name), false])!;
    }
}
