namespace Steward.Core.Tests;

public sealed class AddonCatalogueTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-catalogue-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly IReadOnlyList<ManagedAddon> Configured =
    [
        new("steward", "Steward", "https://addon.hoobi.io/steward/", Features: ["sync"]),
        new("hoobiscripts", "HoobiScripts", "https://addon.hoobi.io/hoobiscripts/", Features: ["hoobiscripts"]),
        new("unflagged", "Unflagged", "https://addon.hoobi.io/unflagged/"),
    ];

    private static readonly HashSet<string> Features = new(["sync"], StringComparer.Ordinal);

    private static readonly CatalogueAddon ActionBars = new(
        "hoobiscripts-actionbars", "HoobiScripts_ActionBars", "https://addon.hoobi.io/hoobiscripts-actionbars/",
        "Hoobi Scripts: ActionBars", "Steward", false, ["hoobiscripts.actionbars"]);

    [Fact]
    public void Visible_WithAServerList_UsesItAsIs()
    {
        IReadOnlyList<ManagedAddon> server = [ActionBars.ToManagedAddon()];

        var visible = AddonCatalogue.Visible(server, Configured, new HashSet<string>());

        Assert.Equal(["hoobiscripts-actionbars"], visible.Select(addon => addon.Id));
    }

    [Fact]
    public void Visible_WithNoServerList_FiltersTheConfiguredListByFeature()
    {
        var visible = AddonCatalogue.Visible(null, Configured, Features);

        Assert.Equal(["steward"], visible.Select(addon => addon.Id));
    }

    [Fact]
    public void Visible_WithAnEmptyServerList_IsEmpty()
    {
        Assert.Empty(AddonCatalogue.Visible([], Configured, Features));
    }

    [Fact]
    public void UpdatesWhileUnreachable_OnlyForAStewardSourcedAddonInTheServerList()
    {
        IReadOnlyList<ManagedAddon> server =
        [
            ActionBars.ToManagedAddon(),
            new("bugsack", "BugSack", "https://api.hoobi.io/guild/api/addons/bugsack/", Source: "GitHub"),
        ];

        Assert.True(AddonCatalogue.UpdatesWhileUnreachable(server, "HoobiScripts-ActionBars"));
        Assert.False(AddonCatalogue.UpdatesWhileUnreachable(server, "bugsack"));
        Assert.False(AddonCatalogue.UpdatesWhileUnreachable(server, "steward"));
        Assert.False(AddonCatalogue.UpdatesWhileUnreachable(null, "hoobiscripts-actionbars"));
    }

    [Fact]
    public void CurseForgeEntry_CarriesItsIconAndIsNotARecordedAddon()
    {
        var bugSack = new CatalogueAddon(
            "bugsack", "BugSack", "https://api.hoobi.io/guild/api/addons/curseforge/6273/88568/", "BugSack", "CurseForge",
            IconUrl: "https://media.forgecdn.net/avatars/thumbnails/62/762/256/256/636142192560849763.jpg").ToManagedAddon();
        var recorded = new ManagedAddon(CurseForgeAddons.Id(6273, 88568), "BugSack", Source: CurseForgeAddons.Source);

        Assert.Equal("https://media.forgecdn.net/avatars/thumbnails/62/762/256/256/636142192560849763.jpg", bugSack.IconUri.AbsoluteUri);
        Assert.False(CurseForgeAddons.IsRecorded(bugSack));
        Assert.True(CurseForgeAddons.IsRecorded(recorded));
    }

    [Fact]
    public void Same_ComparesByValue()
    {
        Assert.True(AddonCatalogue.Same([ActionBars], [ActionBars with { Features = ["hoobiscripts.actionbars"] }]));
        Assert.False(AddonCatalogue.Same([ActionBars], [ActionBars with { AutoInstall = true }]));
        Assert.False(AddonCatalogue.Same(null, []));
        Assert.True(AddonCatalogue.Same(null, null));
    }

    [Fact]
    public void AddonCatalogue_RoundTripsThroughStateJson()
    {
        var path = Path.Combine(_root, "state.json");
        var store = new AppStateStore([], path);

        store.Save(store.Load() with { AddonCatalogue = [ActionBars] });

        Assert.Contains("\"addon_catalogue\"", File.ReadAllText(path));
        Assert.True(AddonCatalogue.Same([ActionBars], store.Load().AddonCatalogue));
    }

    private static ManagedAddon Parse(string json) =>
        System.Text.Json.JsonSerializer.Deserialize(json, CompanionJsonContext.Default.IReadOnlyListCatalogueAddon)![0].ToManagedAddon();

    [Fact]
    public void DeclarativeFields_ParseAndWinOverTheSourceString()
    {
        var addon = Parse("""
            [{"id":"x","folder_name":"X","manifest_base_url":"https://example.test/x/","source":"Steward",
              "manifest_auth":"session","zip_auth":"none","zip_relative":false,"auto_install":true,
              "updates_while_unreachable":false,"source_label":"Somewhere","curseforge":true}]
            """);

        Assert.True(addon.ManifestNeedsSession);
        Assert.False(addon.ZipResolvesAgainstManifest);
        Assert.True(addon.AutoInstall);
        Assert.False(addon.MayUpdateWhileUnreachable);
        Assert.Equal("Somewhere", addon.DisplaySource);
        Assert.True(addon.IsCurseForge);
    }

    [Theory]
    [InlineData("Steward", false, true, true, "Steward", false)]
    [InlineData("CurseForge", true, true, false, "CurseForge", true)]
    [InlineData("Protected", true, false, false, "Steward", false)]
    [InlineData("GitHub", false, true, false, "GitHub", false)]
    public void WithoutTheFields_TheSourceStringDecides(
        string source, bool session, bool zipRelative, bool unreachable, string label, bool curseForge)
    {
        var addon = Parse($$"""[{"id":"x","folder_name":"X","manifest_base_url":"https://example.test/x/","source":"{{source}}"}]""");

        Assert.Equal(session, addon.ManifestNeedsSession);
        Assert.Equal(zipRelative, addon.ZipResolvesAgainstManifest);
        Assert.Equal(unreachable, addon.MayUpdateWhileUnreachable);
        Assert.Equal(label, addon.DisplaySource);
        Assert.Equal(curseForge, addon.IsCurseForge);
    }

    [Fact]
    public void WithoutAnySource_ActsAsSteward()
    {
        var addon = Parse("""[{"id":"x","folder_name":"X","manifest_base_url":"https://example.test/x/"}]""");

        Assert.False(addon.ManifestNeedsSession);
        Assert.True(addon.ZipResolvesAgainstManifest);
        Assert.True(addon.MayUpdateWhileUnreachable);
        Assert.Equal("Steward", addon.DisplaySource);
    }

    [Fact]
    public void UpdatesWhileUnreachable_FollowsTheField()
    {
        IReadOnlyList<ManagedAddon> server =
        [
            (ActionBars with { UpdatesWhileUnreachable = false }).ToManagedAddon(),
            new CatalogueAddon("bugsack", "BugSack", "https://example.test/bugsack/", Source: "CurseForge", UpdatesWhileUnreachable: true).ToManagedAddon(),
        ];

        Assert.False(AddonCatalogue.UpdatesWhileUnreachable(server, "hoobiscripts-actionbars"));
        Assert.True(AddonCatalogue.UpdatesWhileUnreachable(server, "bugsack"));
    }

    [Fact]
    public void DeclarativeFields_AbsentAreNotWrittenToState()
    {
        var json = System.Text.Json.JsonSerializer.Serialize([ActionBars], CompanionJsonContext.Default.IReadOnlyListCatalogueAddon);

        Assert.DoesNotContain("manifest_auth", json, StringComparison.Ordinal);
        Assert.DoesNotContain("source_label", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_ParsesOntoTheManagedAddon()
    {
        var addon = Parse("""
            [{"id":"x","folder_name":"X","manifest_base_url":"https://example.test/x/",
              "notice":{"icon":"lock","text":"Protected addon."}}]
            """);

        Assert.Equal(new AddonNotice("lock", "Protected addon."), addon.Notice);
    }

    [Fact]
    public void Notice_AbsentOrPartial_ParsesAsNullOrOptional()
    {
        Assert.Null(Parse("""[{"id":"x","folder_name":"X","manifest_base_url":"https://example.test/x/"}]""").Notice);

        var partial = Parse("""[{"id":"x","folder_name":"X","manifest_base_url":"https://example.test/x/","notice":{"text":"Hi"}}]""");
        Assert.Equal(new AddonNotice(null, "Hi"), partial.Notice);
    }

    [Fact]
    public void Notice_RoundTripsThroughStateJson_AndIsOmittedWhenNull()
    {
        var path = Path.Combine(_root, "state.json");
        var store = new AppStateStore([], path);
        store.Save(store.Load() with { AddonCatalogue = [ActionBars with { Notice = new AddonNotice("lock", "Protected addon.") }] });

        Assert.Contains("\"notice\"", File.ReadAllText(path));
        Assert.Equal(new AddonNotice("lock", "Protected addon."), store.Load().AddonCatalogue![0].Notice);

        store.Save(store.Load() with { AddonCatalogue = [ActionBars] });

        Assert.DoesNotContain("\"notice\"", File.ReadAllText(path));
        Assert.Null(store.Load().AddonCatalogue![0].Notice);
    }

    [Fact]
    public void AddonCatalogue_AbsentFromStateJson_LoadsAsUnknown()
    {
        var path = Path.Combine(_root, "state.json");
        File.WriteAllText(path, """{"channels":{},"installs":{}}""");

        Assert.Null(new AppStateStore([], path).Load().AddonCatalogue);
    }
}
