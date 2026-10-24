using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using Steward.App.ViewModels;
using Steward.Core;
using Steward.Desktop.Services;

namespace Steward.Desktop.Tests;

public sealed class TukUiAdoptTests(AvaloniaSession session)
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void An_installed_ElvUI_offers_Adopt_and_adopting_it_makes_a_TukUI_row() => session.Run(() =>
    {
        var temp = session.NewFolder();
        var platform = new AvaloniaPlatform();
        using var host = session.NewHost(platform);
        platform.Http = null;
        var main = host.Services.GetRequiredService<MainViewModel>();
        main.UserName = "Hoobi";
        main.IsSignedIn = true;
        main.IsAuthorized = true;
        ((HashSet<string>)typeof(MainViewModel).GetField("_features", Private)!.GetValue(main)!).Add(StewardClient.AddonsFeature);
        typeof(MainViewModel).GetMethod("SetAddonCatalogue", Private)!.Invoke(main, [new List<CatalogueAddon>
        {
            new("steward", "Steward", "https://example.invalid/steward/", "Steward"),
        }]);

        var path = Path.Combine(temp, "_forever_");
        var addOns = Path.Combine(path, "Interface", "AddOns");
        WriteToc(addOns, "ElvUI", "## Interface: 11509, 16001, 120100\n## Title: |cff1784d1ElvUI|r\n## Version: v16.00\n## RequiredDeps: ElvUI_Libraries\n## X-Tukui-ProjectID: -2\n## X-Tukui-ProjectFolders: ElvUI, ElvUI_Libraries, ElvUI_Options\n");
        WriteToc(addOns, "ElvUI_Libraries", "## Interface: 16001\n## Title: ElvUI Libraries\n## Version: v16.00\n");
        WriteToc(addOns, "ElvUI_Options", "## Interface: 16001\n## Title: ElvUI Options\n## Version: v16.00\n## RequiredDeps: ElvUI\n");
        session.Requests.Clear();
        var install = (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", Private)!
            .Invoke(main, [new WowInstall(temp, "_forever_", path, addOns, "wowf", "1.60.1.70245", "World of Warcraft: Forever"), false])!;
        var rescan = install.RescanLocalAsync();
        UiPaths.UiPathWorld.WaitFor(() => rescan.IsCompleted);

        var local = Assert.Single(install.LocalRows, row => row.CanAdopt);
        Assert.Equal("ElvUI", local.DisplayName);
        Assert.Equal("Adopt from TukUI", local.AdoptMenuHeader);

        local.AdoptCommand.Execute(null);
        UiPaths.UiPathWorld.WaitFor(() => install.AddonRows.Any(row => row.AddonId == "tukui--2"));

        var record = Assert.Single(host.Services.GetRequiredService<AppStateStore>().Load().ProviderAddons[path]);
        Assert.Equal("tukui--2", record.Id);
        Assert.Equal(["ElvUI", "ElvUI_Libraries", "ElvUI_Options"], record.Folders);
        var row = install.AddonRows.Single(row => row.AddonId == "tukui--2");
        Assert.Equal("TukUI", row.Source);
        Assert.Equal("https://api.hoobi.io/guild/api/addons/tukui/-2/", row.Addon.ManifestBaseUrl);
        Assert.DoesNotContain(session.Requests, uri => uri.AbsolutePath.Contains("/api/addons/curseforge/", StringComparison.Ordinal));

        main.Installs.Remove(install);
    });

    private static void WriteToc(string addOns, string folder, string toc)
    {
        Directory.CreateDirectory(Path.Combine(addOns, folder));
        File.WriteAllText(Path.Combine(addOns, folder, $"{folder}.toc"), toc);
    }
}
