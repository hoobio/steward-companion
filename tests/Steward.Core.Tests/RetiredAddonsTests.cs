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
        AddonCatalogue = [new CatalogueAddon("steward", "Steward", "https://example.com")],
        ProviderAddons = new(StringComparer.OrdinalIgnoreCase),
    };

    private static Task Delete(string addOnsPath, string folder)
    {
        Directory.Delete(Path.Combine(addOnsPath, folder), recursive: true);
        return Task.CompletedTask;
    }

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
        Seed(Beta, "RXPAuthor");
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
    public async Task Remove_DeletesOnlyTheRetiredFolder_SiblingAndWtfSurvive()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Flavour, "RXPGuides");
        var savedVariables = Path.Combine(Flavour, "WTF", "Account", "ACCOUNT", "SavedVariables", "RXPAuthor.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(savedVariables)!);
        File.WriteAllText(savedVariables, "RXPAuthorDB = {}");
        var found = RetiredAddons.Find(State(RxpAuthor), [Install(Flavour)]);

        var (removed, deferred) = await RetiredAddons.RemoveAsync(found, _ => false, recycle: Delete);

        Assert.Single(removed);
        Assert.Empty(deferred);
        Assert.False(Directory.Exists(Path.Combine(AddOns(Flavour), "RXPAuthor")));
        Assert.True(File.Exists(Path.Combine(AddOns(Flavour), "RXPGuides", "RXPGuides.toc")));
        Assert.True(File.Exists(savedVariables));
    }

    [Fact]
    public async Task Remove_FlavourWithWowRunning_IsDeferredAndLeftOnDisk()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Beta, "RXPAuthor");
        var found = RetiredAddons.Find(State(RxpAuthor), [Install(Flavour), Install(Beta)]);

        var (removed, deferred) = await RetiredAddons.RemoveAsync(found, flavour => flavour == Flavour, recycle: Delete);

        Assert.Equal(Beta, Assert.Single(removed).FlavourPath);
        Assert.Equal(Flavour, Assert.Single(deferred).FlavourPath);
        Assert.True(Directory.Exists(Path.Combine(AddOns(Flavour), "RXPAuthor")));
        Assert.False(Directory.Exists(Path.Combine(AddOns(Beta), "RXPAuthor")));
        Assert.Equal("RXP Author will be uninstalled when World of Warcraft closes.", RetiredAddons.Deferred(deferred));
    }

    [Fact]
    public async Task Remove_RecycleFails_LeavesTheFolderAndReportsIt()
    {
        Seed(Flavour, "RXPAuthor");
        var found = RetiredAddons.Find(State(RxpAuthor), [Install(Flavour)]);
        var failures = new List<RetiredFolder>();

        var (removed, _) = await RetiredAddons.RemoveAsync(found, _ => false, (entry, _) => failures.Add(entry), (_, _) => throw new IOException("no Recycle Bin"));

        Assert.Empty(removed);
        Assert.Single(failures);
        Assert.True(File.Exists(Path.Combine(AddOns(Flavour), "RXPAuthor", "RXPAuthor.toc")));
    }

    [Fact]
    public async Task Remove_ChecksWowInsideTheWriteLockBeforeEachFolder()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Beta, "RXPAuthor");
        var found = RetiredAddons.Find(State(RxpAuthor), [Install(Flavour), Install(Beta)]);
        var checks = new List<bool>();

        await RetiredAddons.RemoveAsync(found, _ =>
        {
            checks.Add(AddonUpdater.AddOnsWriteLock.IsHeldByCurrentThread);
            return false;
        }, recycle: Delete);

        Assert.Equal([true, true], checks);
    }

    [Fact]
    public async Task Remove_ReturnsToTheCallerAndHoldsTheWriteLockUntilTheRecycleFinishes()
    {
        Seed(Flavour, "RXPAuthor");
        var found = RetiredAddons.Find(State(RxpAuthor), [Install(Flavour)]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var removing = RetiredAddons.RemoveAsync(found, _ => false, recycle: (_, _) =>
        {
            started.SetResult();
            return gate.Task;
        });
        await started.Task;
        var entered = AddonUpdater.AddOnsWriteLock.TryEnter();
        if (entered)
        {
            AddonUpdater.AddOnsWriteLock.Exit();
        }

        Assert.False(entered);
        Assert.False(removing.IsCompleted);
        gate.SetResult();
        Assert.Single((await removing).Removed);
    }

    [Fact]
    public async Task RecycleFolders_WowRunning_MovesNothingAndSaysWhy()
    {
        var calls = 0;

        var outcome = await AddonRecycler.RecycleFoldersAsync(AddOns(Flavour), ["RXPAuthor", "RXPAuthor_Data"], () => AddonUpdater.AddOnsWriteLock.IsHeldByCurrentThread, (_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        Assert.Equal(0, calls);
        Assert.Equal(["RXPAuthor", "RXPAuthor_Data"], outcome.Left);
        Assert.Equal("Uninstall failed, nothing was moved: World of Warcraft is running from this install. Close it and try again.", outcome.Failure);
    }

    [Fact]
    public async Task RecycleFolders_OneFolderFails_ReportsWhichMovedAndWhichDidNot()
    {
        var outcome = await AddonRecycler.RecycleFoldersAsync(AddOns(Flavour), ["RXPAuthor", "RXPAuthor_Data", "RXPAuthor_Media"], () => false, (_, folder) =>
            folder == "RXPAuthor_Data" ? throw new IOException("the Recycle Bin could not take it") : Task.CompletedTask);

        Assert.Equal(["RXPAuthor", "RXPAuthor_Media"], outcome.Moved);
        Assert.Equal(["RXPAuthor_Data"], outcome.Left);
        Assert.Equal($"Uninstall partly failed: RXPAuthor, RXPAuthor_Media moved to {AddonRecycler.Destination}, RXPAuthor_Data still in the AddOns folder: the Recycle Bin could not take it", outcome.Failure);
        Assert.Null((await AddonRecycler.RecycleFoldersAsync(AddOns(Flavour), ["RXPAuthor"], () => false, (_, _) => Task.CompletedTask)).Failure);
    }

    [Fact]
    public void PreDeleteResult_AbortsUnlessTheShellWillRecycle()
    {
        Assert.Equal(0, AddonRecycler.PreDeleteResult(0x80));
        Assert.Equal(0, AddonRecycler.PreDeleteResult(0x80 | 0x614));
        Assert.Equal(unchecked((int)0x80004004), AddonRecycler.PreDeleteResult(0));
        Assert.Equal(unchecked((int)0x80004004), AddonRecycler.PreDeleteResult(~0x80u));
    }

    [Fact]
    public void MoveFolder_MoveThrows_CopiesVerifiesThenRemovesTheSource()
    {
        var source = Seed(Flavour, "Moved");
        Directory.CreateDirectory(Path.Combine(source, "Libs", "Empty"));
        File.WriteAllText(Path.Combine(source, "Libs", "lib.lua"), "local x = 1");
        var destination = Path.Combine(_root, "removed", "Moved");

        AddonRecycler.MoveFolder(source, destination, (_, _) => throw new IOException("Invalid cross-device link"));

        Assert.False(Directory.Exists(source));
        Assert.Equal("## Title: x", File.ReadAllText(Path.Combine(destination, "Moved.toc")));
        Assert.Equal("local x = 1", File.ReadAllText(Path.Combine(destination, "Libs", "lib.lua")));
        Assert.True(Directory.Exists(Path.Combine(destination, "Libs", "Empty")));
    }

    [Fact]
    public void Find_FolderInAnyInstallRecordProviderRecordOrCatalogue_IsSkipped()
    {
        Seed(Flavour, "RXPAuthor");
        Seed(Flavour, "HoobiScripts_GamerGuides");
        Seed(Flavour, "Questie");
        var state = State(RxpAuthor, GamerGuides, new RetiredAddon("Questie", "Questie"));
        state.Installs[AppStateStore.Key(Flavour, "old")] = new InstalledAddonRecord("1", "release", null, Folders: ["RXPAuthor"]);
        state.ProviderAddons[Flavour] = [new ProviderAddonRecord("cf", "Other", "Other", "CurseForge", 1, 1, ["HoobiScripts_GamerGuides"])];

        Assert.Equal("Questie", Assert.Single(RetiredAddons.Find(state, [Install(Flavour)])).Folder);
        Assert.Empty(RetiredAddons.Find(state with { AddonCatalogue = [.. state.AddonCatalogue!, new CatalogueAddon("questie", "Questie", "https://example.com")] }, [Install(Flavour)]));
    }

    [Fact]
    public void Find_NoCatalogue_FindsNothing()
    {
        Seed(Flavour, "RXPAuthor");

        Assert.Empty(RetiredAddons.Find(State(RxpAuthor) with { AddonCatalogue = null }, [Install(Flavour)]));
        Assert.Empty(RetiredAddons.Find(State(RxpAuthor) with { AddonCatalogue = [] }, [Install(Flavour)]));
    }

    [Fact]
    public void PruneDeclines_DropsDeclinesWhoseFolderLeftTheList()
    {
        List<string> declines = [AppStateStore.Key(Flavour, "RXPAuthor"), AppStateStore.Key(Beta, "hoobiscripts_gamerguides")];

        Assert.Equal([AppStateStore.Key(Beta, "hoobiscripts_gamerguides")], RetiredAddons.PruneDeclines(declines, [GamerGuides]));
        Assert.Equal(declines, RetiredAddons.PruneDeclines(declines, null));
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData(""","retired_addons":null""", 2)]
    [InlineData(""","retired_addons":[]""", 0)]
    [InlineData(""","retired_addons":[{"folder_name":"HoobiScripts_GamerGuides","name":"Gamer Guides"}]""", 1)]
    public void PruneDeclines_KeepsDeclinesOnlyWhenTheListIsMissingOrNull(string retiredField, int kept)
    {
        var me = JsonSerializer.Deserialize($$"""{"user":{"id":"1","name":"A","username":null,"avatar_url":null,"role":null},"guilds":[]{{retiredField}}}""", CompanionJsonContext.Default.AdminMe)!;
        List<string> declines = [AppStateStore.Key(Flavour, "RXPAuthor"), AppStateStore.Key(Beta, "HoobiScripts_GamerGuides")];

        Assert.Equal(kept, RetiredAddons.PruneDeclines(declines, RetiredAddons.Listed(me.RetiredAddons)).Count);
    }

    [Fact]
    public void Prompt_NamesEachFolderAndFlavour()
    {
        var found = new[] { new RetiredFolder(Flavour, GamerGuides), new RetiredFolder(Beta, GamerGuides), new RetiredFolder(Flavour, RxpAuthor) };
        string Label(string flavour) => flavour == Flavour ? "Forever" : "Beta";

        Assert.Equal(
            $"These addons were retired: Gamer Guides (folder HoobiScripts_GamerGuides, Forever, now part of Steward Guides), Gamer Guides (folder HoobiScripts_GamerGuides, Beta, now part of Steward Guides), RXP Author (folder RXPAuthor, Forever). Uninstall them? Their folders are moved to {AddonRecycler.Destination}.",
            RetiredAddons.Prompt(found, Label));
        Assert.Equal($"This addon was retired: RXP Author (folder RXPAuthor, Forever). Uninstall it? Its folder is moved to {AddonRecycler.Destination}.", RetiredAddons.Prompt([new RetiredFolder(Flavour, RxpAuthor)], Label));
        Assert.Equal("Gamer Guides: Guides moved to Steward Guides.", RetiredAddons.Reasons(found));
    }

    [Fact]
    public async Task Recycle_MovesTheFolderOutOfAddOns()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true", "Sends a real folder to the Recycle Bin, so it runs only on the Windows CI runner");

        var folder = $"StewardRecycleTest{Guid.NewGuid():N}";
        Seed(Flavour, folder);
        Seed(Flavour, "Sibling");

        await AddonRecycler.RecycleAsync(AddOns(Flavour), folder);

        Assert.False(Directory.Exists(Path.Combine(AddOns(Flavour), folder)));
        Assert.True(Directory.Exists(Path.Combine(AddOns(Flavour), "Sibling")));
    }

    [Fact]
    public async Task Recycle_LinkOrUnsafeName_IsRefusedAndLeftInPlace()
    {
        var target = Seed(Path.Combine(_root, "elsewhere"), "Real");
        var link = Path.Combine(Directory.CreateDirectory(AddOns(Flavour)).FullName, "Linked");
        AddonUpdaterTests.Junction(link, target);

        await Assert.ThrowsAsync<IOException>(() => AddonRecycler.RecycleAsync(AddOns(Flavour), "Linked"));
        await Assert.ThrowsAsync<IOException>(() => AddonRecycler.RecycleAsync(AddOns(Flavour), "..."));
        Assert.True(Directory.Exists(link));
        Assert.True(Directory.Exists(AddOns(Flavour)));
        Assert.True(File.Exists(Path.Combine(target, "Real.toc")));
        Directory.Delete(link);
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
