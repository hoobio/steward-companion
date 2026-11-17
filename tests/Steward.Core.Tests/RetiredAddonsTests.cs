using System.Diagnostics;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class RetiredAddonsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-retired-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Flavour => Path.Combine(_root, "_forever_");

    private string Beta => Path.Combine(_root, "_classic_beta_");

    private static string AddOns(string flavour) => Path.Combine(flavour, "Interface", "AddOns");

    private static readonly RetiredAddon GamerGuides = new("HoobiScripts_GamerGuides", "Gamer Guides", "Guides moved to Steward Guides.", "Steward Guides");

    private static readonly RetiredAddon RxpAuthor = new("RXPAuthor", "RXP Author");

    private static AppState State(params RetiredAddon[] retired) => new AppState([], [], null, []) with
    {
        RetiredAddons = [.. retired],
        DeclinedRetiredAddons = [],
        LostAccess = new(StringComparer.OrdinalIgnoreCase),
    };

    private static string Seed(string flavour, string folder)
    {
        var path = Path.Combine(AddOns(flavour), folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, folder + ".toc"), "## Title: x");
        return path;
    }

    private static RetiredInstall Install(string flavour, params string[] managed) => new(flavour, managed);

    [Fact]
    public void Parse_RetiredAddons_DropsBlankFolderNamesAndIgnoresUnknownFields()
    {
        var me = JsonSerializer.Deserialize("""
            {"user":{"id":"1","name":"A","username":null,"avatar_url":null,"role":null},"guilds":[],
             "retired_addons":[
               {"folder_name":"RXPAuthor","name":"RXP Author","reason":null,"replaced_by":"Steward Guides","extra":1},
               {"folder_name":"  ","name":"Blank"},
               {"name":"Missing"}
             ]}
            """, CompanionJsonContext.Default.AdminMe)!;

        var listed = RetiredAddons.Listed(me.RetiredAddons)!;

        var only = Assert.Single(listed);
        Assert.Equal(new RetiredAddon("RXPAuthor", "RXP Author", null, "Steward Guides"), only);
    }

    [Theory]
    [InlineData("""{"user":{"id":"1","name":"A","username":null,"avatar_url":null,"role":null},"guilds":[]}""")]
    [InlineData("""{"user":{"id":"1","name":"A","username":null,"avatar_url":null,"role":null},"guilds":[],"retired_addons":null}""")]
    public void Parse_OldServer_HasNoRetiredList(string body)
    {
        var me = JsonSerializer.Deserialize(body, CompanionJsonContext.Default.AdminMe)!;

        Assert.Null(RetiredAddons.Listed(me.RetiredAddons));
    }

    [Fact]
    public void Find_NoRetiredList_FindsNothing()
    {
        Seed(Flavour, "RXPAuthor");

        Assert.Empty(RetiredAddons.Find(State() with { RetiredAddons = null }, [Install(Flavour)]));
    }

    [Fact]
    public void Find_HandCopiedFolder_IsFoundPerFlavour()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Beta, "rxpauthor");
        Seed(Beta, "HoobiScripts_GamerGuides");

        var found = RetiredAddons.Find(State(GamerGuides, RxpAuthor), [Install(Flavour), Install(Beta)]);

        Assert.Equal([(Flavour, "RXPAuthor"), (Beta, "HoobiScripts_GamerGuides"), (Beta, "RXPAuthor")], found.Select(entry => (entry.FlavourPath, entry.Folder)));
    }

    [Fact]
    public void Find_FolderNotOnDisk_IsSkipped()
    {
        Assert.Empty(RetiredAddons.Find(State(RxpAuthor), [Install(Flavour)]));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    public void Find_NameRemovalRefuses_IsSkipped(string folder)
    {
        Directory.CreateDirectory(Path.Combine(AddOns(Flavour), "a", "b"));

        Assert.Empty(RetiredAddons.Find(State(new RetiredAddon(folder, "Bad")), [Install(Flavour)]));
        Assert.True(Directory.Exists(Path.Combine(AddOns(Flavour), "a", "b")));
    }

    [Fact]
    public void Find_FolderACurrentAddonUses_IsSkipped()
    {
        Seed(Flavour, "RXPAuthor");

        Assert.Empty(RetiredAddons.Find(State(RxpAuthor), [Install(Flavour, "rxpauthor")]));
    }

    [Fact]
    public void Find_FolderWithPendingLostAccess_IsSkipped()
    {
        Seed(Flavour, "RXPAuthor");
        var state = State(RxpAuthor);
        state.LostAccess[AppStateStore.Key(Flavour, "rxp")] = new LostAccessAddon(Flavour, "rxp", "RXP Author", "Steward", ["RXPAuthor"], DateTimeOffset.UnixEpoch);

        Assert.Empty(RetiredAddons.Find(state, [Install(Flavour)]));
    }

    [Fact]
    public void Find_DeclinedFolder_IsSkippedOnlyForThatFlavour()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Beta, "RXPAuthor");
        var state = State(RxpAuthor) with { DeclinedRetiredAddons = [AppStateStore.Key(Flavour, "RXPAuthor")] };

        var found = RetiredAddons.Find(state, [Install(Flavour), Install(Beta)]);

        Assert.Equal(Beta, Assert.Single(found).FlavourPath);
    }

    [Fact]
    public void Find_NewlyRetiredFolder_StillPromptsAfterAnEarlierDecline()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Flavour, "HoobiScripts_GamerGuides");
        var state = State(RxpAuthor, GamerGuides) with { DeclinedRetiredAddons = [AppStateStore.Key(Flavour, "RXPAuthor")] };

        Assert.Equal("HoobiScripts_GamerGuides", Assert.Single(RetiredAddons.Find(state, [Install(Flavour)])).Folder);
    }

    [Fact]
    public void Find_Link_IsSkippedAndItsTargetSurvives()
    {
        var target = Seed(Path.Combine(_root, "elsewhere"), "Real");
        Directory.CreateDirectory(AddOns(Flavour));
        var link = Path.Combine(AddOns(Flavour), "RXPAuthor");
        if (OperatingSystem.IsWindows())
        {
            using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target]) { CreateNoWindow = true, UseShellExecute = false })!;
            mklink.WaitForExit();
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }

        Assert.True(Directory.Exists(link));
        Assert.Empty(RetiredAddons.Find(State(RxpAuthor), [Install(Flavour)]));
        Assert.True(File.Exists(Path.Combine(target, "Real.toc")));
        Directory.Delete(link);
    }

    [Fact]
    public void DeclinedRetiredAddons_PersistAndAreDroppedWithTheirInstall()
    {
        var store = new AppStateStore([], Path.Combine(_root, "state.json"));
        Assert.Empty(store.Load().DeclinedRetiredAddons);

        store.Save(store.Load() with { DeclinedRetiredAddons = [AppStateStore.Key(Flavour, "RXPAuthor"), AppStateStore.Key(Beta, "RXPAuthor")] });

        Assert.Equal([AppStateStore.Key(Flavour, "RXPAuthor"), AppStateStore.Key(Beta, "RXPAuthor")], store.Load().DeclinedRetiredAddons);
        Assert.Equal([AppStateStore.Key(Beta, "RXPAuthor")], AppStateStore.RemoveInstall(store.Load(), Flavour).DeclinedRetiredAddons);
    }

    [Fact]
    public void Remove_DeletesOnlyTheRetiredFolder_SiblingAndWtfSurvive()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Flavour, "RXPGuides");
        var savedVariables = Path.Combine(Flavour, "WTF", "Account", "ACCOUNT", "SavedVariables", "RXPAuthor.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(savedVariables)!);
        File.WriteAllText(savedVariables, "RXPAuthorDB = {}");
        var found = RetiredAddons.Find(State(RxpAuthor), [Install(Flavour)]);

        var (removed, deferred) = RetiredAddons.Remove(found, _ => false);

        Assert.Single(removed);
        Assert.Empty(deferred);
        Assert.False(Directory.Exists(Path.Combine(AddOns(Flavour), "RXPAuthor")));
        Assert.True(File.Exists(Path.Combine(AddOns(Flavour), "RXPGuides", "RXPGuides.toc")));
        Assert.True(File.Exists(savedVariables));
    }

    [Fact]
    public void Remove_FlavourWithWowRunning_IsDeferredAndLeftOnDisk()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Beta, "RXPAuthor");
        var found = RetiredAddons.Find(State(RxpAuthor), [Install(Flavour), Install(Beta)]);

        var (removed, deferred) = RetiredAddons.Remove(found, flavour => flavour == Flavour);

        Assert.Equal(Beta, Assert.Single(removed).FlavourPath);
        Assert.Equal(Flavour, Assert.Single(deferred).FlavourPath);
        Assert.True(Directory.Exists(Path.Combine(AddOns(Flavour), "RXPAuthor")));
        Assert.False(Directory.Exists(Path.Combine(AddOns(Beta), "RXPAuthor")));
        Assert.Equal("RXP Author will be uninstalled when World of Warcraft closes.", RetiredAddons.Deferred(deferred));
    }

    [Fact]
    public void Prompt_ListsEachAddonOnceWithItsReplacement()
    {
        var found = new[] { new RetiredFolder(Flavour, GamerGuides), new RetiredFolder(Beta, GamerGuides), new RetiredFolder(Flavour, RxpAuthor) };

        Assert.Equal("These addons were retired: Gamer Guides (now part of Steward Guides), RXP Author. Uninstall them?", RetiredAddons.Prompt(found));
        Assert.Equal("This addon was retired: RXP Author. Uninstall it?", RetiredAddons.Prompt([new RetiredFolder(Flavour, RxpAuthor)]));
        Assert.Equal("Gamer Guides: Guides moved to Steward Guides.", RetiredAddons.Reasons(found));
    }

    [Fact]
    public void Notice_RetiredFolder_GivesTheReasonAndReplacement()
    {
        var lost = new LostAccessAddon(Flavour, "gamerguides", "Gamer Guides", "Steward", ["HoobiScripts_GamerGuides"], DateTimeOffset.UnixEpoch);

        Assert.Equal("Gamer Guides was removed because it was retired: Guides moved to Steward Guides (now part of Steward Guides)", AccessLoss.Notice(lost, [GamerGuides]));
        Assert.Equal("Gamer Guides was removed because it was retired", AccessLoss.Notice(lost, [GamerGuides with { Reason = null, ReplacedBy = null }]));
        Assert.Equal("Gamer Guides was removed because your access ended", AccessLoss.Notice(lost, [RxpAuthor]));
        Assert.Equal("Gamer Guides was removed because your access ended", AccessLoss.Notice(lost));
    }
}
