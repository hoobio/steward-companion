using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class ManagedFoldersLocalTests(AvaloniaSession session)
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly string[] Folders = ["Nav_Main", "Nav_Data0", "Nav_Data1"];

    [Fact]
    public void Folders_in_a_managed_install_record_are_not_listed_as_Local_addons() => session.Run(() =>
    {
        using var host = session.NewHost(new AvaloniaPlatform { Http = null });
        var (main, install, store) = Setup(session, host);
        store.Save(store.Load() with { Installs = new Dictionary<string, InstalledAddonRecord> { [AppStateStore.Key(install.FlavourPath, "nav")] = new("1.0.0", "release", null, Folders: Folders) } });

        Rescan(install);

        Assert.DoesNotContain(install.LocalRows, row => Folders.Contains(row.FolderName));
        Assert.Contains(install.LocalRows, row => row.FolderName == "Other_Addon");
        main.Installs.Remove(install);
    });

    [Fact]
    public void Folders_named_by_the_release_of_a_row_without_an_install_record_are_not_listed_as_Local_addons() => session.Run(() =>
    {
        using var host = session.NewHost(new AvaloniaPlatform { Http = null });
        var (main, install, _) = Setup(session, host);
        var release = new AddonRelease("1.0.0", null, null, 0, DateTimeOffset.UnixEpoch, Folders: Folders);
        ((Dictionary<string, AddonChannelStatus>)typeof(MainViewModel).GetField("_status", Private)!.GetValue(main)!)["nav"] =
            new AddonChannelStatus(new Dictionary<string, AddonRelease?> { ["release"] = release }, "release", null);

        Rescan(install);

        Assert.DoesNotContain(install.LocalRows, row => Folders.Contains(row.FolderName));
        Assert.Contains(install.LocalRows, row => row.FolderName == "Other_Addon");
        main.Installs.Remove(install);
    });

    private static void Rescan(WowInstallViewModel install)
    {
        var rescan = install.RescanLocalAsync();
        UiPaths.UiPathWorld.WaitFor(() => rescan.IsCompleted);
    }

    private static (MainViewModel Main, WowInstallViewModel Install, AppStateStore Store) Setup(AvaloniaSession session, Microsoft.Extensions.Hosting.IHost host)
    {
        var temp = session.NewFolder();
        var main = host.Services.GetRequiredService<MainViewModel>();
        main.UserName = "Tester";
        main.IsSignedIn = true;
        main.IsAuthorized = true;
        ((HashSet<string>)typeof(MainViewModel).GetField("_features", Private)!.GetValue(main)!).Add(StewardClient.AddonsFeature);
        typeof(MainViewModel).GetMethod("SetAddonCatalogue", Private)!.Invoke(main, [new List<CatalogueAddon>
        {
            new("nav", "Nav_Main", "https://example.invalid/nav/", "Nav"),
        }]);

        var path = Path.Combine(temp, "_forever_");
        var addOns = Path.Combine(path, "Interface", "AddOns");
        const string client = "1.60.1.70245";
        var interfaceNumber = TocFile.InterfaceNumber(client)!.Value;
        foreach (var folder in Folders.Append("Other_Addon"))
        {
            Directory.CreateDirectory(Path.Combine(addOns, folder));
            File.WriteAllText(Path.Combine(addOns, folder, $"{folder}.toc"), $"## Interface: {interfaceNumber}\n## Title: {folder}\n## Version: 1.0.0\n");
        }

        var install = (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", Private)!
            .Invoke(main, [new WowInstall(temp, "_forever_", path, addOns, "wowf", client, "World of Warcraft: Forever"), false])!;
        return (main, install, host.Services.GetRequiredService<AppStateStore>());
    }
}
