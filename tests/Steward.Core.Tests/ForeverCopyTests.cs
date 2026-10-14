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
        Write(Path.Combine(betaWtf, "Account", "1#1", "SavedVariables", "Details.lua"), Old);
        Write(Path.Combine(betaWtf, "Account", "1#1", "SavedVariables", "Details.lua.bak"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "Realm", "Char", "SavedVariables", "Bagnon.lua"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "Realm", "Char", "SavedVariables", "Bagnon.lua.bak"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "bindings-cache.wtf"), New);
        Write(Path.Combine(liveWtf, "Account", "1#1", "macros-cache.txt"), New);
        Write(Path.Combine(betaWtf, "Account", "1#1", "macros-cache.txt"), Old);

        var plan = ForeverCopy.Plan(_beta, _live, []);

        Assert.Equal(
            [Path.Combine("1#1", "Realm", "Char", "SavedVariables", "Bagnon.lua"), Path.Combine("1#1", "SavedVariables", "Details.lua")],
            plan.SavedVariablesFiles.Order(StringComparer.Ordinal));
        Assert.Equal([Path.Combine("1#1", "bindings-cache.wtf")], plan.SettingsFiles);
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
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1", "SavedVariables", "Details.lua"), New, "beta");
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1", "bindings-cache.wtf"), New, "beta");

        var result = ForeverCopy.Sync(_beta, _live, ["Steward"], addons: true, savedVariables: true, settings: true, (_, ex) => throw ex);

        Assert.Equal(new ForeverCopyResult(1, 1, 1, 1, 0), result);
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
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1", "SavedVariables", "Details.lua"), New);
        Write(Path.Combine(_beta.FlavourPath, "WTF", "Account", "1#1", "bindings-cache.wtf"), New);

        var result = ForeverCopy.Sync(_beta, _live, [], addons: true, savedVariables: true, settings: false, (_, ex) => throw ex);

        Assert.Equal((1, 0), (result.SavedVariablesFiles, result.SettingsFiles));
        Assert.False(File.Exists(Path.Combine(_live.FlavourPath, "WTF", "Account", "1#1", "bindings-cache.wtf")));
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
