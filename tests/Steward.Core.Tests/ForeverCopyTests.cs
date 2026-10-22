namespace Steward.Core.Tests;

public sealed class ForeverCopyTests : IDisposable
{
    private static readonly DateTime Old = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Directory.CreateTempSubdirectory("steward-forever-copy-").FullName;
    private readonly WowInstall _beta;
    private readonly WowInstall _live;

    public ForeverCopyTests()
    {
        _beta = Install("_classic_beta_", ForeverLaunch.BetaProduct);
        _live = Install("_forever_", ForeverLaunch.LiveProduct);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private WowInstall Install(string flavour, string product, bool missing = false)
    {
        var path = Path.Combine(_root, flavour);
        return new WowInstall(_root, flavour, path, Path.Combine(path, "Interface", "AddOns"), product, null, product, missing);
    }

    private static void Write(string path, DateTime modified, string contents = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        File.SetLastWriteTimeUtc(path, modified);
    }

    [Fact]
    public void IsGated_LiveWithPresentBetaAndNotDone()
    {
        Assert.True(ForeverCopy.IsGated(_live, [_beta, _live], []));
        Assert.False(ForeverCopy.IsGated(_live, [_beta, _live], [_live.FlavourPath.ToUpperInvariant()]));
        Assert.False(ForeverCopy.IsGated(_live, [_live], []));
        Assert.False(ForeverCopy.IsGated(_live, [_beta with { IsMissing = true }, _live], []));
        Assert.False(ForeverCopy.IsGated(_beta, [_beta, _live], []));
    }

    [Fact]
    public void Plan_CopiesMissingFolders_UpdatesUnmanagedOlderOnes_SkipsManaged()
    {
        Write(Path.Combine(_beta.AddOnsPath, "Questie", "Questie.toc"), New);
        Write(Path.Combine(_beta.AddOnsPath, "Details", "Details.toc"), New);
        Write(Path.Combine(_live.AddOnsPath, "Details", "Details.toc"), Old);
        Write(Path.Combine(_beta.AddOnsPath, "Bagnon", "Bagnon.toc"), Old);
        Write(Path.Combine(_live.AddOnsPath, "Bagnon", "Bagnon.toc"), New);
        Write(Path.Combine(_beta.AddOnsPath, "Steward", "Steward.toc"), New);
        Write(Path.Combine(_live.AddOnsPath, "Steward", "Steward.toc"), Old);

        var plan = ForeverCopy.Plan(_beta, _live, ["Steward"]);

        Assert.Equal(["Questie"], plan.NewFolders);
        Assert.Equal(["Details"], plan.UpdatedFolders);
    }

    [Fact]
    public void Plan_SplitsAccountFilesIntoSavedVariablesAndSettings_SkipsBackupsAndConfig()
    {
        var betaWtf = Path.Combine(_beta.FlavourPath, "WTF");
        var liveWtf = Path.Combine(_live.FlavourPath, "WTF");
        Write(Path.Combine(betaWtf, "Config.wtf"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "SavedVariables", "Steward.lua"), Old);
        Write(Path.Combine(liveWtf, "Account", "1#1", "SavedVariables", "Steward.lua"), New);
        Write(Path.Combine(betaWtf, "Account", "1#2", "SavedVariables", "Steward.lua"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "SavedVariables", "Details.lua"), Old);
        Write(Path.Combine(betaWtf, "Account", "1#1", "SavedVariables", "Details.lua.bak"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "Realm", "Char", "SavedVariables", "Bagnon.lua"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "Realm", "Char", "SavedVariables", "Bagnon.lua.bak"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "bindings-cache.wtf"), New);
        Write(Path.Combine(liveWtf, "Account", "1#1", "macros-cache.txt"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "macros-cache.txt"), Old);
        Write(Path.Combine(betaWtf, "Account", "1#1", "config-cache.old"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "Realm", "Char", "cache.md5"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "Realm", "Char", "layout-local.txt"), New);

        var plan = ForeverCopy.Plan(_beta, _live, []);

        Assert.Equal([Path.Combine("1#1", "SavedVariables", "Details.lua")], plan.SavedVariablesFiles);
        Assert.Equal([Path.Combine("1#1", "bindings-cache.wtf")], plan.SettingsFiles);
        Assert.True(plan.SettingsNewer);
    }

    [Fact]
    public void Plan_OffersSettingsThatDifferWhateverTheirDate_SkipsIdenticalAndUnlistedFiles()
    {
        var beta = Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1");
        var live = Path.Combine(_live.FlavourPath, "WTF", "Account", "1#1");
        Write(Path.Combine(beta, "edit-mode-cache-account.txt"), Old, "beta layout");
        Write(Path.Combine(live, "edit-mode-cache-account.txt"), New, "default layout");
        Write(Path.Combine(beta, "config-cache.wtf"), New, "same");
        Write(Path.Combine(live, "config-cache.wtf"), Old, "same");
        Write(Path.Combine(beta, "character-list-order.txt"), New, "beta characters");
        Write(Path.Combine(beta, "cache.md5"), New, "beta checksums");

        var plan = ForeverCopy.Plan(_beta, _live, []);

        Assert.Equal([Path.Combine("1#1", "edit-mode-cache-account.txt")], plan.SettingsFiles);
        Assert.False(plan.SettingsNewer);
    }

    [Fact]
    public void Sync_StampsCopiedSettingsInCacheMd5_KeepsOtherSlots()
    {
        var beta = Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1");
        var live = Path.Combine(_live.FlavourPath, "WTF", "Account", "1#1");
        Write(Path.Combine(beta, "bindings-cache.wtf"), Old, "bind UP TOGGLEAUTORUN");
        Write(Path.Combine(live, "bindings-cache.wtf"), New, "");
        var liveSlots = Enumerable.Range(0, 240).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(Path.Combine(live, "cache.md5"), liveSlots);

        ForeverCopy.Sync(_beta, _live, [], addons: false, savedVariables: false, settings: true, clientSettings: false, (_, ex) => throw ex);

        var slots = File.ReadAllBytes(Path.Combine(live, "cache.md5"));
        Assert.Equal(Convert.FromHexString("53902DBF37E9DA72E3E62D57F4AED271"), slots[24..40]);
        Assert.Equal(liveSlots[..24], slots[..24]);
        Assert.Equal(liveSlots[40..], slots[40..]);
    }

    [Fact]
    public void Plan_ListsGraphicsAndSoundSettingsThatDiffer()
    {
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Config.wtf"), New,
            "SET graphicsQuality \"2\"\r\nSET Sound_MasterVolume \"0.5\"\r\nSET vsync \"1\"\r\nSET portal \"beta\"\r\nSET textLocale \"enGB\"\r\nSET farclip \"1000\"\r\nSET accessibilityScreenNarrationEnabled \"1\"\r\n");
        Write(Path.Combine(_live.FlavourPath, "WTF", "Config.wtf"), Old, "SET GRAPHICSQUALITY \"6\"\r\nSET vsync \"1\"\r\nSET portal \"\"\r\n");

        var plan = ForeverCopy.Plan(_beta, _live, []);

        Assert.Equal(["accessibilityScreenNarrationEnabled", "farclip", "graphicsQuality", "Sound_MasterVolume"], plan.ClientSettings.Order(StringComparer.OrdinalIgnoreCase));
        Assert.True(plan.ClientSettingsNewer);
        Assert.False(plan.LiveConfigMissing);
    }

    [Fact]
    public void Plan_OffersNoGraphicsSettingsWhenEitherConfigIsMissing()
    {
        Assert.Empty(ForeverCopy.Plan(_beta, _live, []).ClientSettings);

        Write(Path.Combine(_beta.FlavourPath, "WTF", "Config.wtf"), New, "SET graphicsQuality \"2\"\r\n");
        var plan = ForeverCopy.Plan(_beta, _live, []);

        Assert.Empty(plan.ClientSettings);
        Assert.True(plan.LiveConfigMissing);
        Assert.False(ForeverCopy.Sync(_beta, _live, [], addons: false, savedVariables: false, settings: false, clientSettings: true, (_, ex) => throw ex).ClientSettings > 0);
        Assert.False(File.Exists(Path.Combine(_live.FlavourPath, "WTF", "Config.wtf")));
    }

    [Fact]
    public void Sync_MergesGraphicsAndSoundIntoLiveConfig_KeepsEveryOtherLine()
    {
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Config.wtf"), New,
            "SET graphicsQuality \"2\"\r\nSET Sound_MasterVolume \"0.5\"\r\nSET portal \"beta\"\r\nSET textLocale \"enGB\"\r\n");
        var liveConfig = Path.Combine(_live.FlavourPath, "WTF", "Config.wtf");
        Write(liveConfig, Old, "SET portal \"\"\r\nSET graphicsQuality \"6\"\r\nSET textLocale \"enUS\"\r\n");

        var result = ForeverCopy.Sync(_beta, _live, [], addons: false, savedVariables: false, settings: false, clientSettings: true, (_, ex) => throw ex);

        Assert.Equal(2, result.ClientSettings);
        Assert.Equal("SET portal \"\"\r\nSET graphicsQuality \"2\"\r\nSET textLocale \"enUS\"\r\nSET Sound_MasterVolume \"0.5\"\r\n", File.ReadAllText(liveConfig));
        Assert.Empty(ForeverCopy.Plan(_beta, _live, []).ClientSettings);
    }

    [Fact]
    public void Sync_CopiesNewerAndNeverDeletes()
    {
        Write(Path.Combine(_beta.AddOnsPath, "Questie", "Questie.toc"), New, "beta");
        Write(Path.Combine(_beta.AddOnsPath, "Details", "Details.toc"), New, "beta");
        Write(Path.Combine(_live.AddOnsPath, "Details", "Details.toc"), Old, "live");
        Write(Path.Combine(_live.AddOnsPath, "Details", "LiveOnly.lua"), Old, "live");
        Write(Path.Combine(_beta.AddOnsPath, "Steward", "Steward.toc"), New, "beta");
        Write(Path.Combine(_live.AddOnsPath, "Steward", "Steward.toc"), Old, "live");
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Config.wtf"), New, "beta");
        Directory.CreateDirectory(Path.Combine(_live.FlavourPath, "WTF", "Account", "1#1"));
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1", "SavedVariables", "Details.lua"), New, "beta");
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1", "bindings-cache.wtf"), New, "beta");

        var result = ForeverCopy.Sync(_beta, _live, ["Steward"], addons: true, savedVariables: true, settings: true, clientSettings: true, (_, ex) => throw ex);

        Assert.Equal(new ForeverCopyResult(1, 1, 1, 1, 0, 0), result);
        Assert.False(File.Exists(Path.Combine(_live.FlavourPath, "WTF", "Config.wtf")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(_live.AddOnsPath, "Questie", "Questie.toc")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(_live.AddOnsPath, "Details", "Details.toc")));
        Assert.Equal("live", File.ReadAllText(Path.Combine(_live.AddOnsPath, "Details", "LiveOnly.lua")));
        Assert.Equal("live", File.ReadAllText(Path.Combine(_live.AddOnsPath, "Steward", "Steward.toc")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(_live.FlavourPath, "WTF", "Account", "1#1", "bindings-cache.wtf")));
        Assert.Empty(ForeverCopy.Plan(_beta, _live, ["Steward"]).NewFolders.Concat(ForeverCopy.Plan(_beta, _live, ["Steward"]).UpdatedFolders));
    }

    [Fact]
    public void Sync_TakesOnlyTheChosenAccountFiles()
    {
        Directory.CreateDirectory(Path.Combine(_live.FlavourPath, "WTF", "Account", "1#1"));
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1", "SavedVariables", "Details.lua"), New);
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1", "bindings-cache.wtf"), New);

        var result = ForeverCopy.Sync(_beta, _live, [], addons: true, savedVariables: true, settings: false, clientSettings: false, (_, ex) => throw ex);

        Assert.Equal((1, 0), (result.SavedVariablesFiles, result.SettingsFiles));
        Assert.False(File.Exists(Path.Combine(_live.FlavourPath, "WTF", "Account", "1#1", "bindings-cache.wtf")));
    }

    private string BetaAccount(string name) => Path.Combine(_beta.FlavourPath, "WTF", "Account", name);

    private string LiveAccount(string name) => Path.Combine(_live.FlavourPath, "WTF", "Account", name);

    [Fact]
    public void DefaultAccountMap_MatchesSameNamesIgnoringCase()
    {
        Directory.CreateDirectory(BetaAccount("Abc#1"));
        Directory.CreateDirectory(BetaAccount("Abc#2"));
        Directory.CreateDirectory(LiveAccount("ABC#1"));
        Directory.CreateDirectory(LiveAccount("OTHER"));
        Directory.CreateDirectory(LiveAccount("EXTRA"));

        var map = ForeverCopy.DefaultAccountMap(_beta, _live);

        Assert.Equal("ABC#1", map["Abc#1"]);
        Assert.Null(map["Abc#2"]);
    }

    [Fact]
    public void DefaultAccountMap_PairsTheOnlyUnmappedBetaAccountWithTheOnlyFreeLiveOne()
    {
        Directory.CreateDirectory(BetaAccount("1#1"));
        Directory.CreateDirectory(LiveAccount("JIMMYBOBOZ"));

        Assert.Equal("JIMMYBOBOZ", ForeverCopy.DefaultAccountMap(_beta, _live)["1#1"]);
    }

    [Fact]
    public void DefaultAccountMap_SkipsAStaleNumberedFolderBesideALegacyOne()
    {
        Directory.CreateDirectory(BetaAccount("54939295#1"));
        Directory.CreateDirectory(Path.Combine(LiveAccount("54939295#1"), "SavedVariables"));
        Directory.CreateDirectory(LiveAccount("JIMMYBOBOZ"));

        Assert.Equal("JIMMYBOBOZ", ForeverCopy.DefaultAccountMap(_beta, _live)["54939295#1"]);
        Assert.Contains(ForeverCopy.Plan(_beta, _live, []).LiveAccounts, account => account.Folder == "54939295#1");
    }

    [Fact]
    public void DefaultAccountMap_GivesALoneClassicAccountNameTheFirstBetaAccount()
    {
        Directory.CreateDirectory(BetaAccount("54939295#5"));
        Directory.CreateDirectory(BetaAccount("54939295#1"));
        Directory.CreateDirectory(BetaAccount("54939295#10"));
        Directory.CreateDirectory(LiveAccount("JIMMYBOBOZ"));

        var map = ForeverCopy.DefaultAccountMap(_beta, _live);

        Assert.Equal("JIMMYBOBOZ", map["54939295#1"]);
        Assert.Null(map["54939295#5"]);
        Assert.Null(map["54939295#10"]);
    }

    [Fact]
    public void DefaultAccountMap_IgnoresTheClientsAccountLevelSavedVariablesFolder()
    {
        Directory.CreateDirectory(BetaAccount("54939295#10"));
        Directory.CreateDirectory(Path.Combine(LiveAccount("54939295#10"), "SavedVariables"));
        Directory.CreateDirectory(LiveAccount("SavedVariables"));

        Assert.Equal("54939295#10", ForeverCopy.DefaultAccountMap(_beta, _live)["54939295#10"]);
        Assert.DoesNotContain(ForeverCopy.Plan(_beta, _live, []).LiveAccounts, account => account.Folder == "SavedVariables");
        Assert.False(ForeverCopy.NeedsAccountMigration(_beta, _live));
    }

    [Fact]
    public void NeedsAccountMigration_TrueForAStaleNumberedFolderBesideALegacyOne()
    {
        Directory.CreateDirectory(BetaAccount("54939295#1"));
        Directory.CreateDirectory(LiveAccount("54939295#1"));
        Directory.CreateDirectory(LiveAccount("JIMMYBOBOZ"));

        Assert.True(ForeverCopy.NeedsAccountMigration(_beta, _live));
        Assert.Equal("JIMMYBOBOZ", ForeverCopy.FirstLegacyAccount(_live));
    }

    [Fact]
    public void NeedsAccountMigration_FalseWithOnlyNumberedLiveFolders()
    {
        Directory.CreateDirectory(BetaAccount("1#1"));
        Directory.CreateDirectory(LiveAccount("1#1"));
        Directory.CreateDirectory(Path.Combine(LiveAccount("1#2"), "Realm", "Char"));

        Assert.False(ForeverCopy.NeedsAccountMigration(_beta, _live));
    }

    [Fact]
    public void NeedsAccountMigration_FalseWhenTheBetaAccountAlreadyMapsToTheLegacyFolder()
    {
        Directory.CreateDirectory(BetaAccount("1#1"));
        Directory.CreateDirectory(LiveAccount("JIMMYBOBOZ"));

        Assert.False(ForeverCopy.NeedsAccountMigration(_beta, _live));
    }

    [Theory]
    [InlineData("54939295#1", false)]
    [InlineData("JIMMYBOBOZ", true)]
    [InlineData("1#a", true)]
    public void IsLegacyAccountName_IsAnythingButDigitsHashDigits(string folder, bool legacy) =>
        Assert.Equal(legacy, ForeverCopy.IsLegacyAccountName(folder));

    [Fact]
    public void DefaultAccountMap_LeavesAmbiguousAccountsUnmapped()
    {
        Directory.CreateDirectory(BetaAccount("1#1"));
        Directory.CreateDirectory(BetaAccount("1#2"));
        Directory.CreateDirectory(LiveAccount("ONE"));
        Directory.CreateDirectory(LiveAccount("TWO"));

        Assert.All(ForeverCopy.DefaultAccountMap(_beta, _live).Values, Assert.Null);
    }

    [Fact]
    public void DefaultAccountMap_IsAllNullWithoutLiveAccounts()
    {
        Directory.CreateDirectory(BetaAccount("1#1"));

        var plan = ForeverCopy.Plan(_beta, _live, []);

        Assert.All(plan.AccountMap.Values, Assert.Null);
        Assert.Empty(plan.LiveAccounts);
        Assert.Empty(plan.SavedVariablesFiles);
    }

    [Fact]
    public void Plan_ListsCharactersPerAccount()
    {
        Write(Path.Combine(BetaAccount("1#1"), "Realm", "Hoobi", "layout-local.txt"), New);
        Write(Path.Combine(BetaAccount("1#1"), "Other", "Grug", "layout-local.txt"), New);
        Write(Path.Combine(BetaAccount("1#1"), "Other", "Hoobi", "layout-local.txt"), New);
        Write(Path.Combine(BetaAccount("1#1"), "SavedVariables", "Details.lua"), New);

        var account = Assert.Single(ForeverCopy.Plan(_beta, _live, []).BetaAccounts);

        Assert.Equal("1#1", account.Folder);
        Assert.Equal(["Grug", "Hoobi"], account.Characters);
    }

    [Fact]
    public void Sync_CopiesIntoTheMappedAccountAndStampsItsCacheMd5()
    {
        Write(Path.Combine(BetaAccount("1#1"), "SavedVariables", "Details.lua"), New, "beta");
        Write(Path.Combine(BetaAccount("1#1"), "bindings-cache.wtf"), New, "bind UP TOGGLEAUTORUN");
        Directory.CreateDirectory(LiveAccount("JIMMYBOBOZ"));
        Directory.CreateDirectory(LiveAccount("SIGGY72"));
        var map = new Dictionary<string, string?> { ["1#1"] = "SIGGY72" };

        var result = ForeverCopy.Sync(_beta, _live, [], addons: false, savedVariables: true, settings: true, clientSettings: false, (_, ex) => throw ex, map);

        Assert.Equal((1, 1), (result.SavedVariablesFiles, result.SettingsFiles));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(LiveAccount("SIGGY72"), "SavedVariables", "Details.lua")));
        Assert.Equal(Convert.FromHexString("53902DBF37E9DA72E3E62D57F4AED271"), File.ReadAllBytes(Path.Combine(LiveAccount("SIGGY72"), "cache.md5"))[24..40]);
        Assert.Empty(Directory.GetFileSystemEntries(LiveAccount("JIMMYBOBOZ")));
        Assert.False(Directory.Exists(LiveAccount("1#1")));
    }

    private string SiblingAccount(string sibling, string account) => Path.Combine(_root, sibling, "WTF", "Account", account);

    [Fact]
    public void Plan_OffersAClassicAccountNameFromASiblingGameAndPairsItWithTheFirstBetaAccount()
    {
        Directory.CreateDirectory(BetaAccount("54939295#2"));
        Directory.CreateDirectory(BetaAccount("54939295#1"));
        Directory.CreateDirectory(SiblingAccount("_classic_", "JIMMYBOBOZ"));
        Directory.CreateDirectory(SiblingAccount("_retail_", "jimmyboboz"));

        var plan = ForeverCopy.Plan(_beta, _live, []);

        Assert.Equal([("JIMMYBOBOZ", "_classic_")], plan.LiveAccounts.Select(account => (account.Folder, account.FoundIn)));
        Assert.Equal("JIMMYBOBOZ", plan.AccountMap["54939295#1"]);
        Assert.Null(plan.AccountMap["54939295#2"]);
        Assert.Equal("JIMMYBOBOZ", ForeverCopy.DefaultAccountMap(_beta, _live)["54939295#1"]);
        Assert.False(ForeverCopy.NeedsAccountMigration(_beta, _live));
    }

    [Fact]
    public void Plan_OffersNoCandidatesWhenLiveHasALegacyFolder()
    {
        Directory.CreateDirectory(LiveAccount("OWN"));
        Directory.CreateDirectory(SiblingAccount("_classic_", "JIMMYBOBOZ"));

        var account = Assert.Single(ForeverCopy.Plan(_beta, _live, []).LiveAccounts);

        Assert.Equal(("OWN", null), (account.Folder, account.FoundIn));
    }

    [Fact]
    public void Plan_IgnoresSavedVariablesNumberedFoldersAndTheBetaInSiblingScans()
    {
        Directory.CreateDirectory(SiblingAccount("_classic_", "SavedVariables"));
        Directory.CreateDirectory(SiblingAccount("_classic_", "54939295#1"));
        Directory.CreateDirectory(BetaAccount("BETAONLY"));

        Assert.Empty(ForeverCopy.Plan(_beta, _live, []).LiveAccounts);
    }

    [Fact]
    public void Sync_CreatesTheCandidateAccountOnLiveAndStampsItsCacheMd5()
    {
        Write(Path.Combine(BetaAccount("1#1"), "SavedVariables", "Details.lua"), New, "beta");
        Write(Path.Combine(BetaAccount("1#1"), "bindings-cache.wtf"), New, "bind UP TOGGLEAUTORUN");
        Directory.CreateDirectory(SiblingAccount("_classic_", "JIMMYBOBOZ"));

        var result = ForeverCopy.Sync(_beta, _live, [], addons: false, savedVariables: true, settings: true, clientSettings: false, (_, ex) => throw ex);

        Assert.Equal((1, 1), (result.SavedVariablesFiles, result.SettingsFiles));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(LiveAccount("JIMMYBOBOZ"), "SavedVariables", "Details.lua")));
        Assert.True(File.Exists(Path.Combine(LiveAccount("JIMMYBOBOZ"), "cache.md5")));
    }

    [Fact]
    public void Sync_CopiesNothingForAnUnmappedAccount()
    {
        Write(Path.Combine(BetaAccount("1#1"), "SavedVariables", "Details.lua"), New);
        Write(Path.Combine(BetaAccount("1#1"), "bindings-cache.wtf"), New);
        Directory.CreateDirectory(LiveAccount("1#1"));
        var map = new Dictionary<string, string?> { ["1#1"] = null };

        var result = ForeverCopy.Sync(_beta, _live, [], addons: false, savedVariables: true, settings: true, clientSettings: false, (_, ex) => throw ex, map);

        Assert.Equal((0, 0), (result.SavedVariablesFiles, result.SettingsFiles));
        Assert.Empty(Directory.GetFileSystemEntries(LiveAccount("1#1")));
    }

    [Fact]
    public void CarryState_RekeysRecordsForFoldersOnLive_NeverReplacesLiveKeys()
    {
        const string beta = @"C:\wow\_classic_beta_";
        const string live = @"C:\wow\_forever_";
        var questie = new ProviderAddonRecord("curseforge-334372-88568", "Questie", "Questie", CurseForgeAddons.Source, 334372, 88568, ["Questie"]);
        var details = new ProviderAddonRecord("curseforge-61284-88568", "Details", "Details", CurseForgeAddons.Source, 61284, 88568, ["Details"]);
        var otherClient = new ProviderAddonRecord("curseforge-1-99999", "Bagnon", "Bagnon", CurseForgeAddons.Source, 1, 99999, ["Bagnon"]);
        var liveSteward = new InstalledAddonRecord("2.0", "release", null);
        var state = new AppState(
            [],
            new Dictionary<string, InstalledAddonRecord>
            {
                [AppStateStore.Key(beta, questie.Id)] = new("1.0", "release", null),
                [AppStateStore.Key(beta, "steward")] = new("1.0", "release", null),
                [AppStateStore.Key(live, "steward")] = liveSteward,
                [AppStateStore.Key(beta, "restedxp")] = new("4.0", "release", null),
                [AppStateStore.Key(beta, otherClient.Id)] = new("1.0", "release", null),
            },
            IgnoredAddons: [AppStateStore.Key(beta, questie.Id), AppStateStore.Key(beta, "restedxp")])
        {
            ProviderAddons = new() { [beta] = [questie, details, otherClient] },
            KeptLocalAddons = [AppStateStore.Key(beta, "WeakAuras"), AppStateStore.Key(beta, "Gone")],
        };
        var onLive = new HashSet<string>(["Questie", "Bagnon", "Steward", "WeakAuras"], StringComparer.OrdinalIgnoreCase);
        var catalogue = new Dictionary<string, string> { ["steward"] = "Steward", ["restedxp"] = "RXPGuides" };

        var carried = ForeverCopy.CarryState(state, beta, live, 88568, onLive.Contains, catalogue.GetValueOrDefault);

        Assert.Equal([questie.Id], carried.ProviderAddons[live].Select(record => record.Id));
        Assert.Equal("1.0", carried.Installs[AppStateStore.Key(live, questie.Id)].Version);
        Assert.Same(liveSteward, carried.Installs[AppStateStore.Key(live, "steward")]);
        Assert.False(carried.Installs.ContainsKey(AppStateStore.Key(live, "restedxp")));
        Assert.False(carried.Installs.ContainsKey(AppStateStore.Key(live, otherClient.Id)));
        Assert.Contains(AppStateStore.Key(live, questie.Id), carried.IgnoredAddons);
        Assert.DoesNotContain(AppStateStore.Key(live, "restedxp"), carried.IgnoredAddons);
        Assert.Contains(AppStateStore.Key(live, "WeakAuras"), carried.KeptLocalAddons);
        Assert.DoesNotContain(AppStateStore.Key(live, "Gone"), carried.KeptLocalAddons);
        Assert.Equal(3, carried.ProviderAddons[beta].Count);
    }
}
