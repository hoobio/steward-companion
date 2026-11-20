using Microsoft.Extensions.Logging.Abstractions;

using Steward.App.Services;
using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.Desktop.Tests;

public sealed class ForcedGuidesTests : IDisposable
{
    private const string Retail = "RestedXP Trailblazer Retail Beta Build";

    private static readonly WowInstall Install = new("root", @"C:\wow\_forever_", "flavour", "addons", "wowf", "1.0", "Forever");

    private readonly string _root = Directory.CreateTempSubdirectory("steward-forced-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_forced_guide_is_allowed_and_forced_where_it_was_not()
    {
        var service = NewService();
        Assert.False(service.IsAllowed(Install, Retail));

        service.SetForced(Install.FlavourPath, Retail, true);

        Assert.True(service.IsAllowed(Install, Retail));
        Assert.True(service.IsForced(Install, Retail));
    }

    [Fact]
    public void A_forced_guide_persists_through_a_save_and_load()
    {
        NewService().SetForced(Install.FlavourPath, Retail, true);

        var reloaded = NewService();

        Assert.True(reloaded.IsAllowed(Install, Retail));
        Assert.True(reloaded.IsForced(Install, Retail));
    }

    [Fact]
    public void Stopping_the_force_makes_the_guide_disallowed_again()
    {
        var service = NewService();
        service.SetForced(Install.FlavourPath, Retail, true);

        service.SetForced(Install.FlavourPath, Retail, false);

        Assert.False(service.IsAllowed(Install, Retail));
        Assert.False(service.IsForced(Install, Retail));
        Assert.False(NewService().IsForced(Install, Retail));
    }

    [Fact]
    public void A_forced_row_is_enabled_and_ticked_so_it_is_written_and_says_it_is_forced()
    {
        var card = new RestedXpInstallViewModel(Install, _ => Task.CompletedTask, null!);

        card.SetProducts([Retail], [Retail], new Dictionary<string, long>(), new Dictionary<string, Uri>(), _ => true, _ => true);

        var row = Assert.Single(card.Rows);
        Assert.True(row.IsAllowed);
        Assert.True(row.IsSelected);
        Assert.Equal([Retail], card.SelectedProducts);
        Assert.Equal("Forced for this client", row.UpdatedText);
        Assert.False(row.ForceVisibility);
        Assert.True(row.StopForcingVisibility);
    }

    [Fact]
    public void An_unforced_row_is_disabled_unticked_and_offers_force_install()
    {
        var card = new RestedXpInstallViewModel(Install, _ => Task.CompletedTask, null!);

        card.SetProducts([Retail], [Retail], new Dictionary<string, long>(), new Dictionary<string, Uri>(), _ => false, _ => false);

        var row = Assert.Single(card.Rows);
        Assert.False(row.IsAllowed);
        Assert.False(row.IsSelected);
        Assert.Empty(card.SelectedProducts);
        Assert.Equal("Not for this client", row.UpdatedText);
        Assert.True(row.ForceVisibility);
        Assert.False(row.StopForcingVisibility);
    }

    [Fact]
    public async Task The_row_menu_commands_ask_the_card_to_force_and_to_stop_forcing()
    {
        var calls = new List<(string Product, bool Forced)>();
        var card = new RestedXpInstallViewModel(Install, _ => Task.CompletedTask, null!, (_, product, forced) =>
        {
            calls.Add((product, forced));
            return Task.CompletedTask;
        });
        card.SetProducts([Retail], [], new Dictionary<string, long>(), new Dictionary<string, Uri>(), _ => false);
        var row = card.Rows[0];

        await row.ForceInstallCommand.ExecuteAsync(null);
        await row.StopForcingCommand.ExecuteAsync(null);

        Assert.Equal([(Retail, true), (Retail, false)], calls);
    }

    private RestedXpService NewService() => new(
        new RestedXpClient(new HttpClient(), "https://account.test", "https://guides.test", "https://token.test", "client", "scope"),
        new AppStateStore([], Path.Combine(_root, "state.json")),
        new Dictionary<string, string[]> { ["wowf"] = ["Forever"], ["wow"] = [""] },
        Path.Combine(_root, "guides"),
        NullLogger<RestedXpService>.Instance);
}
