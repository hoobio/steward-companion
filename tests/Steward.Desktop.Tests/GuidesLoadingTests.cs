using Steward.App.ViewModels;
using Steward.Core;

namespace Steward.Desktop.Tests;

public sealed class GuidesLoadingTests
{
    private static readonly WowInstall Install = new("root", "_forever_", "flavour", "addons", "wowf", "1.0", "Forever");

    [Fact]
    public void A_signed_in_session_with_no_catalogue_is_loading() =>
        Assert.True(RestedXpViewModel.IsCatalogueLoading(signedIn: true, hasCatalogue: false, fetchFailed: false));

    [Fact]
    public void A_cached_catalogue_never_shows_loading_during_a_refresh() =>
        Assert.False(RestedXpViewModel.IsCatalogueLoading(signedIn: true, hasCatalogue: true, fetchFailed: false));

    [Fact]
    public void A_failed_fetch_and_a_signed_out_session_are_not_loading()
    {
        Assert.False(RestedXpViewModel.IsCatalogueLoading(signedIn: true, hasCatalogue: false, fetchFailed: true));
        Assert.False(RestedXpViewModel.IsCatalogueLoading(signedIn: false, hasCatalogue: false, fetchFailed: false));
    }

    [Fact]
    public void Loading_then_products_shows_the_rows()
    {
        var card = Loading();
        Load(card, ["Forever Leveling Guide"]);

        Assert.False(card.LoadingVisibility);
        Assert.False(card.NoGuidesVisibility);
        Assert.Single(card.Rows);
    }

    [Fact]
    public void Loading_then_no_products_shows_no_guides()
    {
        var card = Loading();
        Load(card, []);

        Assert.False(card.LoadingVisibility);
        Assert.True(card.NoGuidesVisibility);
    }

    [Fact]
    public void A_failed_first_fetch_shows_neither_loading_nor_no_guides()
    {
        var card = Loading();
        card.LoadingVisibility = RestedXpViewModel.IsCatalogueLoading(true, false, fetchFailed: true);

        Assert.False(card.LoadingVisibility);
        Assert.False(card.NoGuidesVisibility);
    }

    [Fact]
    public void Loading_change_is_notified_for_bindings()
    {
        var card = new RestedXpInstallViewModel(Install, _ => Task.CompletedTask, null!);
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.LoadingVisibility = true;
        card.IsCatalogueLoaded = true;

        Assert.Contains(nameof(RestedXpInstallViewModel.LoadingVisibility), changed);
        Assert.Contains(nameof(RestedXpInstallViewModel.NoGuidesVisibility), changed);
    }

    private static RestedXpInstallViewModel Loading()
    {
        var card = new RestedXpInstallViewModel(Install, _ => Task.CompletedTask, null!);
        card.SetProducts([], [], new Dictionary<string, long>(), new Dictionary<string, Uri>(), _ => true);
        card.LoadingVisibility = RestedXpViewModel.IsCatalogueLoading(true, card.IsCatalogueLoaded, false);

        Assert.True(card.LoadingVisibility);
        Assert.False(card.NoGuidesVisibility);
        return card;
    }

    private static void Load(RestedXpInstallViewModel card, IReadOnlyList<string> products)
    {
        card.SetProducts(products, [], new Dictionary<string, long>(), new Dictionary<string, Uri>(), _ => true);
        card.IsCatalogueLoaded = true;
        card.LoadingVisibility = RestedXpViewModel.IsCatalogueLoading(true, card.IsCatalogueLoaded, false);
    }
}
