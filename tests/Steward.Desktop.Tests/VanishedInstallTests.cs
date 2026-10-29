using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.Desktop.Tests;

public sealed class VanishedInstallTests(AvaloniaSession session)
{
    [Fact]
    public void Rescan_drops_a_discovered_install_whose_folder_was_deleted() => session.Run(() =>
    {
        var temp = session.NewFolder();
        using var host = session.NewHost(new Services.AvaloniaPlatform());
        var main = host.Services.GetRequiredService<MainViewModel>();
        main.DiscoverWowInstalls = (_, _) => [];
        var kept = AddInstall(main, temp, "_forever_", "wowf");
        var deleted = AddInstall(main, temp, "_retail_", "wow");

        Directory.Delete(deleted.FlavourPath, recursive: true);
        main.RescanCommand.Execute(null);

        Assert.Equal([kept.FlavourPath], main.Installs.Select(install => install.FlavourPath));
        main.Installs.Remove(kept);
    });

    private static WowInstallViewModel AddInstall(MainViewModel main, string temp, string flavour, string product)
    {
        var path = Path.Combine(temp, flavour);
        var addOns = Directory.CreateDirectory(Path.Combine(path, "Interface", "AddOns")).FullName;
        return (WowInstallViewModel)typeof(MainViewModel)
            .GetMethod("AddInstall", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [new WowInstall(temp, flavour, path, addOns, product, "1.60.1.70245", flavour), false])!;
    }
}
