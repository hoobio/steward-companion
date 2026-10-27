using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class AddonSettingsTests : IDisposable
{
    private const string Character = "Hoobi Spellbook-Classic Beta PvP";

    private readonly string _temp = Directory.CreateTempSubdirectory("steward-settings-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static SettingsSchema Schema() => SettingsSchema.Parse(File.ReadAllText(Fixture("hoobiscripts-settings-schema.json")));

    private static IReadOnlyDictionary<string, LuaValue> Globals() => AddonSettings.Load(Fixture("HoobiScripts.lua"));

    private static SettingsRow Row(string key) => Schema().Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!).Single(r => r.Key == key);

    private static string? NoClass(string? character) => null;

    internal static bool DeepEqual(LuaValue a, LuaValue b) => a.Kind == b.Kind && a.Kind switch
    {
        LuaKind.Table => a.Table.Count == b.Table.Count && a.Table.Zip(b.Table).All(pair =>
            (pair.First.Key is null ? pair.Second.Key is null : pair.Second.Key is not null && DeepEqual(pair.First.Key, pair.Second.Key))
            && DeepEqual(pair.First.Value, pair.Second.Value)),
        _ => AddonSettings.Same(a, b),
    };

    private static void AssertSameGlobals(IReadOnlyDictionary<string, LuaValue> expected, IReadOnlyDictionary<string, LuaValue> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        Assert.All(expected, global => Assert.True(DeepEqual(global.Value, actual[global.Key]), $"{global.Key} differs"));
    }

    private static LuaValue Settings(IReadOnlyDictionary<string, LuaValue> globals) => globals["HoobiScriptsDB"].GetTable("settings")!;

    [Fact]
    public void Fixture_RoundTripsThroughTheParserAndWriter()
    {
        var first = Globals();
        var written = LuaWriter.SerializeGlobals(first);
        var second = LuaSavedVariables.Parse(written);

        AssertSameGlobals(first, second);
        Assert.Equal(written, LuaWriter.SerializeGlobals(second));
        Assert.Equal(LuaKind.Nil, second["HoobiScriptsCharDB"].Kind);
        Assert.Equal(-270.0009155273438, second["HoobiScriptsDB"].GetTable("threatPoint")!.Items[2].Number);
        Assert.Equal(9007199254740992d, Settings(second).GetNumber("bigCounter"));
        Assert.Equal(1.5e-07, Settings(second).GetNumber("tiny"));
        Assert.Equal("|cFF33FF99BugSack:|r said \"hi\" to C:\\Games\\WoW\nand left", second["HoobiScriptsDB"].GetTable("chat")!.Items[0].GetString("t"));
    }

    [Fact]
    public void Write_KeepsNonAsciiBytesExactly()
    {
        var file = Path.Combine(_temp, "HoobiScripts.lua");
        File.Copy(Fixture("HoobiScripts.lua"), file);

        AddonSettings.Write(file, AddonSettings.Load(file), () => false);

        var bytes = File.ReadAllBytes(file);
        Assert.Contains("Gr\u00fcg Aa", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        AssertSameGlobals(Globals(), AddonSettings.Load(file));
    }

    [Fact]
    public void Schema_ParsesEveryRowType()
    {
        var schema = Schema();
        var rows = schema.Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!).ToList();

        Assert.False(schema.IsReadOnly);
        Assert.Equal("HoobiScripts", schema.Folder);
        Assert.Equal(["bitmask", "checkbox", "dropdown", "mailTarget", "money", "note", "text"], rows.Select(r => r.Type).Distinct().Order(StringComparer.Ordinal));
        Assert.All(rows, row => Assert.True(row.IsKnownType));
        Assert.Equal("vendorOldFood", Row("vendorOldWater").Parent);
        Assert.Equal(["Details"], Row("threatMeter").LockedWhenLoaded);
        Assert.Equal("hoobi", Row("skinAccent").VisibleWhen!.Value.GetString());
        Assert.True(Row("questMobSparkles").KeepDefault);
        Assert.True(Row("vendorPlainWater").IsCharacter);
        Assert.Equal(3600, Row("reenableEnemyNameplates").Rule!.Max);
    }

    [Fact]
    public void Schema_FlagsANewerVersionAndAnUnknownRowType()
    {
        var schema = SettingsSchema.Parse("""{"schemaVersion":2,"folder":"HoobiScripts","pages":[{"title":"P","sections":[{"column":1,"rows":[{"type":"slider","key":"k"}]}]}]}""");

        Assert.True(schema.IsReadOnly);
        Assert.False(schema.Pages![0].Sections![0].Rows![0].IsKnownType);
    }

    [Fact]
    public void Resolve_ReadsTheFileThenTheClassDefaultThenTheDefault()
    {
        var globals = Globals();

        Assert.Equal("all", AddonSettings.Resolve(globals, Row("autoRepair"), null, null).Text);
        Assert.Equal("Gr\u00fcg Aa", AddonSettings.Resolve(globals, Row("mailTradeGoodsTarget"), null, null).Text);
        Assert.False(AddonSettings.Resolve(globals, Row("hideErrors") with { Key = "missing" }, null, null).Boolean);
        Assert.True(AddonSettings.Resolve(globals, Row("vendorPlainWater"), "Gr\u00fcg Aa-Classic Beta PvP", "WARRIOR").Boolean);
        Assert.False(AddonSettings.Resolve(globals, Row("vendorPlainWater"), "Hoobi Quester-Classic Beta PvP", "DRUID").Boolean);
        Assert.False(AddonSettings.Resolve(globals, Row("vendorPlainWater"), Character, null).Boolean);
    }

    [Fact]
    public void Characters_AreTheCharsKeysWithTheirClass()
    {
        var globals = Globals();

        Assert.Equal(["Gr\u00fcg Aa-Classic Beta PvP", "Hoobi Quester-Classic Beta PvP", Character], AddonSettings.Characters(globals));
        Assert.Equal("WARRIOR", AddonSettings.ClassOf(globals, "Gr\u00fcg Aa-Classic Beta PvP"));
        Assert.Null(AddonSettings.ClassOf(globals, Character));
        Assert.Equal("Hoobi Spellbook (Classic Beta PvP)", AddonSettings.CharacterLabel(Character));
    }

    [Fact]
    public void CharactersOn_LeavesOutTheBetaRealmOnTheLiveClientOnly()
    {
        IReadOnlyList<string> characters = ["Grug Aa-classic beta pvp", Character, "Hoobi Drood-Nightslayer", "NoRealm"];

        Assert.Equal(["Hoobi Drood-Nightslayer", "NoRealm"], AddonSettings.CharactersOn(characters, ForeverLaunch.LiveProduct));
        Assert.Equal(characters, AddonSettings.CharactersOn(characters, ForeverLaunch.BetaProduct));
        Assert.Equal(characters, AddonSettings.CharactersOn(characters, null));
    }

    [Fact]
    public void Apply_ChangesOnlyTheSchemaKeyAndKeepsEverythingElse()
    {
        var before = Globals();

        var after = AddonSettings.Apply(before, [new SettingChange(Row("autoRepair"), null, LuaValue.FromString("own"))], NoClass);

        Assert.Equal("own", Settings(after).GetString("autoRepair"));
        var restored = AddonSettings.Apply(after, [new SettingChange(Row("autoRepair"), null, LuaValue.FromString("all"))], NoClass);
        AssertSameGlobals(before, restored);
    }

    [Fact]
    public void Apply_RemovesAValueEqualToItsDefault()
    {
        var after = AddonSettings.Apply(Globals(), [new SettingChange(Row("skin"), null, LuaValue.FromString("blizzard"))], NoClass);

        Assert.Null(Settings(after).Get("skin"));
        Assert.Equal("all", Settings(after).GetString("autoRepair"));
    }

    [Fact]
    public void Apply_KeepsATableLeftEmpty()
    {
        var globals = LuaSavedVariables.Parse("""HoobiScriptsDB = { ["settings"] = { ["skin"] = "hoobi" } }""");

        var after = AddonSettings.Apply(globals, [new SettingChange(Row("skin"), null, LuaValue.FromString("blizzard"))], NoClass);

        Assert.Empty(Settings(after).Table);
    }

    [Fact]
    public void Apply_WritesADefaultWhenTheRowKeepsIt()
    {
        var after = AddonSettings.Apply(Globals(), [new SettingChange(Row("questMobSparkles"), null, LuaValue.FromBoolean(false))], NoClass);

        Assert.Equal(LuaKind.Boolean, Settings(after).Get("questMobSparkles")!.Kind);
    }

    [Fact]
    public void Apply_DoesNothingWhenADefaultIsAlreadyAbsent()
    {
        var before = Globals();

        var after = AddonSettings.Apply(before, [new SettingChange(Row("worldSparkles"), null, LuaValue.FromBoolean(true))], NoClass);

        AssertSameGlobals(before, after);
    }

    [Fact]
    public void Apply_AlwaysWritesAClassSeededKeyWithNoClassKnown()
    {
        var after = AddonSettings.Apply(Globals(), [new SettingChange(Row("vendorPlainWater"), "New Person-Classic Beta PvP", LuaValue.FromBoolean(false))], NoClass);

        var entry = after["HoobiScriptsDB"].GetTable("chars")!.GetTable("New Person-Classic Beta PvP")!;
        Assert.False(entry.Get("vendorPlainWater")!.Boolean);
        Assert.Single(entry.Table);
    }

    [Fact]
    public void Apply_RemovesAClassSeededKeyAtTheClassDefault()
    {
        var quester = "Hoobi Quester-Classic Beta PvP";
        var seeded = AddonSettings.Apply(Globals(), [new SettingChange(Row("vendorPlainWater"), quester, LuaValue.FromBoolean(true))], _ => "DRUID");
        var reset = AddonSettings.Apply(seeded, [new SettingChange(Row("vendorPlainWater"), quester, LuaValue.FromBoolean(false))], _ => "DRUID");

        Assert.True(seeded["HoobiScriptsDB"].GetTable("chars")!.GetTable(quester)!.Get("vendorPlainWater")!.Boolean);
        Assert.Null(reset["HoobiScriptsDB"].GetTable("chars")!.GetTable(quester)!.Get("vendorPlainWater"));
    }

    [Fact]
    public void Apply_EncodesNonAsciiTextAndCharacterKeys()
    {
        var character = "J\u00f6rg Aa-Classic Beta PvP";
        var after = AddonSettings.Apply(
            Globals(),
            [
                new SettingChange(Row("mailTradeGoodsTarget"), null, LuaValue.FromString("J\u00f6rg Aa")),
                new SettingChange(Row("vendorPlainWater"), character, LuaValue.FromBoolean(true)),
            ],
            NoClass);

        Assert.Equal("J\u00f6rg Aa", AddonSettings.Resolve(after, Row("mailTradeGoodsTarget"), null, null).Text);
        Assert.Contains(character, AddonSettings.Characters(after));
        Assert.Contains("J\u00c3\u00b6rg Aa", LuaWriter.SerializeGlobals(after), StringComparison.Ordinal);
    }

    [Fact]
    public void AddCharacter_WritesTheEntryWithClassSeededKeysOnly()
    {
        var character = "Grug Aa-Classic Beta PvP";
        var rows = Schema().Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!);

        var after = AddonSettings.AddCharacter(Globals(), character, null, rows);

        var entry = after["HoobiScriptsDB"].GetTable("chars")!.GetTable(character)!;
        Assert.Equal(["vendorPlainWater"], entry.Table.Select(e => e.Key!.Text));
        Assert.Contains(character, AddonSettings.Characters(after));
        Assert.Equal("all", Settings(after).GetString("autoRepair"));
    }

    [Fact]
    public void AddCharacter_WithAClassWritesOnlyTheClass()
    {
        var character = "Grug Aa-Classic Beta PvP";
        var rows = Schema().Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!);

        var after = AddonSettings.AddCharacter(Globals(), character, "WARRIOR", rows);

        var entry = after["HoobiScriptsDB"].GetTable("chars")!.GetTable(character)!;
        Assert.Equal(["class"], entry.Table.Select(e => e.Key!.Text));
        Assert.Equal("WARRIOR", AddonSettings.ClassOf(after, character));
        Assert.True(AddonSettings.Resolve(after, Row("vendorPlainWater"), character, "WARRIOR").Boolean);
    }

    [Fact]
    public void AddCharacter_CreatesTheTablesInAnEmptyFile()
    {
        var after = AddonSettings.AddCharacter(new Dictionary<string, LuaValue>(), "Grug Aa-Realm", null, []);

        Assert.Equal(["Grug Aa-Realm"], AddonSettings.Characters(after));
    }

    private static SettingsSchema RealSchema(string folder) =>
        SettingsSchema.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-schemas", folder + ".json")));

    [Theory]
    [InlineData("HoobiScripts")]
    [InlineData("HoobiScripts_ActionBars")]
    [InlineData("HoobiScripts_GamerGuides")]
    [InlineData("HoobiScripts_Sniper")]
    [InlineData("StewardAuctioning")]
    [InlineData("StewardBuffManager")]
    [InlineData("StewardJourney")]
    public void RealSchema_ParsesWithEveryRowKnown(string folder)
    {
        var schema = RealSchema(folder);
        var rows = schema.Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!).ToList();

        Assert.Equal(folder, schema.Folder);
        Assert.False(schema.IsReadOnly);
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.True(row.IsKnownType, $"{row.Key} {row.Type} {row.Scope}"));
        Assert.All(rows.Where(row => row.IsSetting), row => Assert.NotEmpty(row.Path!));
    }

    [Fact]
    public void RealSchema_CarriesTheClassAndCharacterFileScopes()
    {
        var core = RealSchema("HoobiScripts").Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!).ToList();
        var classRow = RealSchema("HoobiScripts_ActionBars").Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!).Single(row => row.IsClass);
        var journey = RealSchema("StewardJourney").Pages![0].Sections![0].Rows![0];

        Assert.Equal(AddonSettings.ClassToken, classRow.Key);
        Assert.True(journey.IsCharacterFile);
        Assert.Contains(core, row => row.DynamicOptions);
        Assert.Contains(core, row => row.VisibleWhenLoaded is { Count: > 0 });
        Assert.Equal(["HoobiScripts_ActionBars"], core.Single(row => row.Key == "actionBars").HiddenWhenLoaded);
        var byClass = core.First(row => row.OptionsByClass is not null);
        Assert.NotEqual(byClass.OptionsFor(null), byClass.OptionsFor("DRUID"));
    }

    [Fact]
    public void ClassScope_ReadsAndWritesUnderTheClassToken()
    {
        var row = RealSchema("HoobiScripts_ActionBars").Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!).Single(r => r.IsClass);
        var globals = LuaSavedVariables.Parse("""HoobiScripts_ActionBarsDB = { ["classEnabled"] = { ["MAGE"] = false } }""");

        Assert.False(AddonSettings.Resolve(globals, row, Character, "MAGE").Boolean);
        Assert.True(AddonSettings.Resolve(globals, row, Character, "DRUID").Boolean);
        Assert.Null(AddonSettings.Stored(globals, row, Character, null));

        var after = AddonSettings.Apply(globals, [new SettingChange(row, Character, LuaValue.FromBoolean(false))], _ => "DRUID");
        Assert.False(after["HoobiScripts_ActionBarsDB"].GetTable("classEnabled")!.Get("DRUID")!.Boolean);
        Assert.Throws<InvalidOperationException>(() => AddonSettings.Apply(globals, [new SettingChange(row, Character, LuaValue.FromBoolean(false))], _ => null));
    }

    [Fact]
    public void CharacterFolder_FindsTheFolderOfAKnownRealm()
    {
        var account = Account(Path.Combine("91", "Hoobi-Spellbook"));
        var realms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Classic Beta PvP"] = "91" };

        Assert.Equal(Path.Combine(account, "91", "Hoobi-Spellbook"), AddonSettings.CharacterFolder(account, Character, realms));
        Assert.Null(AddonSettings.CharacterFolder(account, "Other Person-Classic Beta PvP", realms));
        Assert.Null(AddonSettings.CharacterFolder(account, "Hoobi Spellbook-Unknown", realms));
        Assert.Equal(
            Path.Combine(account, "91", "Hoobi-Spellbook", "SavedVariables", "StewardJourney.lua"),
            AddonSettings.CharacterFilePath(Path.Combine(account, "91", "Hoobi-Spellbook"), "StewardJourney"));
    }

    [Fact]
    public void AnyInstalled_MatchesNamesAndStarPrefixesIgnoringCase()
    {
        var addOns = Path.Combine(_temp, "AddOns");
        Directory.CreateDirectory(Path.Combine(addOns, "EllesmereUIForeverEssentials"));
        Directory.CreateDirectory(Path.Combine(addOns, "RXPGuides"));

        Assert.True(AddonSettings.AnyInstalled(addOns, ["Ellesmere*"]));
        Assert.True(AddonSettings.AnyInstalled(addOns, ["ellesmere*"]));
        Assert.True(AddonSettings.AnyInstalled(addOns, ["rxpguides"]));
        Assert.False(AddonSettings.AnyInstalled(addOns, ["Ellesmere"]));
        Assert.False(AddonSettings.AnyInstalled(addOns, ["Details", "Bartender*"]));
        Assert.False(AddonSettings.AnyInstalled(addOns, null));
    }

    [Theory]
    [InlineData("MiXeD", true, null, "mixed", null)]
    [InlineData("  MiXeD  ", true, null, "mixed", null)]
    [InlineData(" Keep ", null, null, "Keep", null)]
    [InlineData("", null, false, null, "Enter a value")]
    [InlineData("   ", null, false, null, "Enter a value")]
    [InlineData("", null, true, "", null)]
    [InlineData("", null, null, "", null)]
    public void Check_AppliesTheStringRule(string input, bool? lower, bool? empty, string? stored, string? error)
    {
        var row = Row("skin") with { Type = "text", Rule = new SettingsRule("string", null, null, empty, lower ?? false) };

        Assert.Equal(error, AddonSettings.Check(row, input, out var value));
        Assert.Equal(stored, value?.Text);
    }

    [Fact]
    public void Apply_CreatesTheGlobalWhenTheFileIsMissing()
    {
        var after = AddonSettings.Apply(AddonSettings.Load(Path.Combine(_temp, "none.lua")), [new SettingChange(Row("skin"), null, LuaValue.FromString("hoobi"))], NoClass);

        Assert.Equal("hoobi", Settings(after).GetString("skin"));
    }

    [Fact]
    public void Write_RefusesWhileTheClientRuns()
    {
        var file = Path.Combine(_temp, "HoobiScripts.lua");
        File.WriteAllText(file, "HoobiScriptsDB = {}\n");
        var checks = 0;

        Assert.Throws<InvalidOperationException>(() => AddonSettings.Write(file, Globals(), () => true));
        Assert.Throws<InvalidOperationException>(() => AddonSettings.Write(file, Globals(), () => checks++ > 0));

        Assert.Equal("HoobiScriptsDB = {}\n", File.ReadAllText(file));
        Assert.Equal([file], Directory.GetFiles(_temp));
    }

    [Fact]
    public void Write_NeverCreatesAMissingFile()
    {
        var file = AddonSettings.FilePath(_temp, "1#1", "HoobiScripts");

        Assert.Throws<InvalidOperationException>(() => AddonSettings.Write(file, Globals(), () => false));

        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }

    [Theory]
    [InlineData("30", null, "30")]
    [InlineData(" 30 ", null, "30")]
    [InlineData("", null, "")]
    [InlineData("0", "Enter a whole number from 1 to 3600", null)]
    [InlineData("3601", "Enter a whole number from 1 to 3600", null)]
    [InlineData("ten", "Enter a whole number from 1 to 3600", null)]
    public void Check_AppliesTheIntegerRule(string input, string? error, string? stored)
    {
        Assert.Equal(error, AddonSettings.Check(Row("reenableEnemyNameplates"), input, out var value));
        Assert.Equal(stored, value?.Text);
    }

    [Fact]
    public void Check_StoresANumberWhenTheDefaultIsOne()
    {
        var row = Row("reenableEnemyNameplates") with { Default = System.Text.Json.JsonDocument.Parse("0").RootElement };

        AddonSettings.Check(row, "12", out var value);

        Assert.Equal(12, value!.Number);
    }

    [Theory]
    [InlineData("Gr\u00fcg Aa", null)]
    [InlineData("O'Neil", null)]
    [InlineData("Grug2", "Enter a character name")]
    public void Check_AppliesTheCharacterNameRule(string input, string? error) =>
        Assert.Equal(error, AddonSettings.Check(Row("mailTradeGoodsTarget"), input, out _));

    [Theory]
    [InlineData(0, "0c")]
    [InlineData(25050, "2g 50s 50c")]
    [InlineData(25000, "2g 50s")]
    [InlineData(10001, "1g 1c")]
    public void FormatMoney_ShowsGoldSilverAndCopper(double copper, string text) => Assert.Equal(text, AddonSettings.FormatMoney(copper));

    [Theory]
    [InlineData("2g 50s", 25000L)]
    [InlineData("2g 50s 50c", 25050L)]
    [InlineData("3", 30000L)]
    [InlineData("99c", 99L)]
    [InlineData("1g 100s", null)]
    [InlineData("1x", null)]
    [InlineData("", null)]
    [InlineData("-5g", null)]
    [InlineData("9999999g 99s 99c", 99_999_999_999L)]
    [InlineData("10000000g", null)]
    [InlineData("1000000000000000g", null)]
    [InlineData("922337203685477g", null)]
    public void ParseMoney_ReadsGoldSilverAndCopper(string text, long? copper) => Assert.Equal(copper, AddonSettings.ParseMoney(text));

    private string Account(params string[] characterFolders)
    {
        var account = Path.Combine(_temp, "54939295#1");
        Directory.CreateDirectory(Path.Combine(account, "SavedVariables"));
        foreach (var folder in characterFolders)
        {
            Directory.CreateDirectory(Path.Combine(account, folder));
        }

        return account;
    }

    [Fact]
    public void KnownRealms_PairsACharacterFolderWithItsCharsKey()
    {
        var account = Account(Path.Combine("91", "Hoobi-Spellbook"), Path.Combine("91", "Stray-Folder"));

        var realms = AddonSettings.KnownRealms(account, [Character, "Hoobi Quester-Other Realm"]);

        Assert.Equal("91", Assert.Single(realms).Value);
        Assert.Equal("Classic Beta PvP", realms.Keys.Single());
    }

    [Fact]
    public void KnownRealms_IsEmptyWithNoMatch()
    {
        var account = Account(Path.Combine("91", "Someone-Else"));

        Assert.Empty(AddonSettings.KnownRealms(account, [Character]));
        Assert.Empty(AddonSettings.KnownRealms(Path.Combine(_temp, "missing"), [Character]));
    }

    [Fact]
    public void KnownRealms_KeepsHyphensInTheRealmAndIgnoresCase()
    {
        var account = Account(Path.Combine("12", "hoobi-drood"), Path.Combine("7", "Grug-Aa"));

        var realms = AddonSettings.KnownRealms(account, ["Hoobi Drood-Azjol-Nerub", "Grug Aa-Classic Beta PvP"]);

        Assert.Equal("12", realms["azjol-nerub"]);
        Assert.Equal("7", realms["Classic Beta PvP"]);
    }

    [Fact]
    public void KnownRealms_SkipsANameOnTwoRealms()
    {
        var account = Account(Path.Combine("91", "Hoobi-Drood"));

        Assert.Empty(AddonSettings.KnownRealms(account, ["Hoobi Drood-Realm A", "Hoobi Drood-Realm B"]));
    }

    [Fact]
    public void KnownRealms_SkipsARealmSeenUnderTwoIds()
    {
        var account = Account(Path.Combine("91", "Hoobi-Drood"), Path.Combine("92", "Grug-Aa"));

        Assert.Empty(AddonSettings.KnownRealms(account, ["Hoobi Drood-Realm A", "Grug Aa-Realm A"]));
    }

    [Fact]
    public void CreateCharacterFolder_UsesTheGameNameForm()
    {
        var account = Account();

        var folder = AddonSettings.CreateCharacterFolder(account, "91", " grug", "aa ");

        Assert.Equal(Path.Combine(account, "91", "Grug-Aa"), folder);
        Assert.True(Directory.Exists(folder));
        Assert.Equal("Grug Aa-Classic Beta PvP", AddonSettings.CharacterKey("grug", "aa", "Classic Beta PvP"));
    }

    [Theory]
    [InlineData("mcDonald", "McDonald")]
    [InlineData(" vanDer ", "VanDer")]
    [InlineData("AA", "AA")]
    [InlineData("o'neil", "O'neil")]
    public void NamePart_CapitalisesOnlyTheFirstLetter(string typed, string expected) => Assert.Equal(expected, AddonSettings.NamePart(typed));

    [Theory]
    [InlineData("Gr\u00fcg", null)]
    [InlineData(" O'Neil ", null)]
    [InlineData("Jean-Luc", "Use letters and apostrophes only")]
    [InlineData("", "Enter a name")]
    [InlineData("   ", "Enter a name")]
    [InlineData("Grug Aa", "Use letters and apostrophes only")]
    [InlineData("Grug2", "Use letters and apostrophes only")]
    [InlineData("Abcdefghijklm", "Use 12 letters or fewer")]
    public void CheckNamePart_AllowsLettersAndApostrophesOnly(string typed, string? error) => Assert.Equal(error, AddonSettings.CheckNamePart(typed));

    private string AddOn(string folder, string toc)
    {
        var path = Directory.CreateDirectory(Path.Combine(_temp, "AddOns", folder)).FullName;
        File.WriteAllText(Path.Combine(path, folder + ".toc"), toc);
        return Path.Combine(_temp, "AddOns");
    }

    [Fact]
    public void SavedVariableOwners_MapsEachGlobalToTheFolderWhoseTocDeclaresIt()
    {
        AddOn("HoobiScripts", "## Title: |cff33ff99Hoobi|r Scripts\n## SavedVariablesPerCharacter: HoobiScriptsCharDB, HoobiScriptsRouteDB\n## SavedVariables: HoobiScriptsDB\n");
        AddOn("HoobiScripts_ActionBars", "## SavedVariables: HoobiScripts_ActionBarsDB\n");
        AddOn("StewardJourney", "## SavedVariablesPerCharacter: StewardJourneyDB\n");
        var addOns = AddOn("Copycat", "## SavedVariables: SharedDB\n");
        AddOn("Copycat2", "## SavedVariables: SharedDB\n");

        var owners = AddonSettings.SavedVariableOwners(addOns, ["HoobiScripts", "HoobiScripts_ActionBars", "StewardJourney", "Copycat", "Copycat2", "Missing"]);
        var rows = RealSchema("HoobiScripts_ActionBars").Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!).ToList();
        var journey = RealSchema("StewardJourney").Pages![0].Sections![0].Rows![0];

        Assert.Equal(new SavedVariableOwner("HoobiScripts", "Hoobi Scripts", false), AddonSettings.OwnerOf(owners, rows.Single(row => row.Key == "actionBars")));
        Assert.Equal("HoobiScripts_ActionBars", AddonSettings.OwnerOf(owners, rows.Single(row => row.Key == "applyAccount"))!.Folder);
        Assert.Equal("HoobiScripts_ActionBars", AddonSettings.OwnerOf(owners, rows.Single(row => row.IsClass))!.Folder);
        Assert.Equal(new SavedVariableOwner("StewardJourney", "StewardJourney", true), AddonSettings.OwnerOf(owners, journey));
        Assert.True(owners["HoobiScriptsCharDB"]!.PerCharacter);
        Assert.Null(owners["SharedDB"]);
        Assert.Null(AddonSettings.OwnerOf(owners, journey with { Path = ["HoobiScriptsDB"] }));
        Assert.Null(AddonSettings.OwnerOf(owners, rows[0] with { Path = ["HoobiScriptsCharDB"] }));
        Assert.Null(AddonSettings.OwnerOf(owners, rows[0] with { Path = ["SharedDB"] }));
        Assert.Null(AddonSettings.OwnerOf(owners, rows[0] with { Path = ["UnknownDB"] }));
    }

    [Theory]
    [InlineData("HoobiScripts", null)]
    [InlineData("hoobiscripts", null)]
    [InlineData("HoobiScripts_ActionBars", "its folder HoobiScripts is not HoobiScripts_ActionBars")]
    public void Refusal_RequiresTheCatalogueFolder(string folderName, string? reason) =>
        Assert.Equal(reason, RealSchema("HoobiScripts").Refusal(folderName));

    [Theory]
    [InlineData("""["..\\..\\evil"]""")]
    [InlineData("""["HoobiScriptsDB\n"]""")]
    [InlineData("""["Hoobi ScriptsDB"]""")]
    [InlineData("""["1DB"]""")]
    [InlineData("""["{character}"]""")]
    [InlineData("""[null]""")]
    [InlineData("""["HoobiScriptsDB", ""]""")]
    [InlineData("""["HoobiScriptsDB", null]""")]
    public void Refusal_RejectsAPathThatIsNotAGlobalAndTableKeys(string path)
    {
        var schema = SettingsSchema.Parse($$"""{"schemaVersion":1,"folder":"HoobiScripts","pages":[{"title":"P","sections":[{"column":1,"rows":[{"type":"checkbox","key":"k","path":{{path}}}]}]}]}""");

        Assert.NotNull(schema.Refusal("HoobiScripts"));
    }

    [Fact]
    public void Refusal_AcceptsEveryRealSchema()
    {
        foreach (var folder in new[] { "HoobiScripts", "HoobiScripts_ActionBars", "HoobiScripts_GamerGuides", "HoobiScripts_Sniper", "StewardAuctioning", "StewardBuffManager", "StewardJourney" })
        {
            Assert.Null(RealSchema(folder).Refusal(folder));
        }
    }

    [Fact]
    public void AbsentKeptDefaults_PicksAKeepDefaultRowWhoseKeyIsAbsent()
    {
        var rows = RealSchema("HoobiScripts").Pages!.SelectMany(p => p.Sections!).SelectMany(s => s.Rows!).ToList();
        var sparkles = rows.Single(row => row.Key == "questMobSparkles");
        var candidates = new[] { new SettingChange(sparkles, null, LuaValue.FromBoolean(true)), new SettingChange(rows.Single(row => row.Key == "hideErrors"), null, LuaValue.FromBoolean(false)) };

        var absent = Assert.Single(AddonSettings.AbsentKeptDefaults(Globals(), candidates, NoClass));
        var stored = AddonSettings.Apply(Globals(), [absent], NoClass);

        Assert.True(Settings(stored).Get("questMobSparkles")!.Boolean);
        Assert.Empty(AddonSettings.AbsentKeptDefaults(stored, candidates, NoClass));
    }

    [Fact]
    public async Task SettingsSchema_IsFetchedBesideTheManifestAndRevalidated()
    {
        var json = File.ReadAllText(Fixture("hoobiscripts-settings-schema.json"));
        var requests = new List<(string Uri, string? IfNoneMatch)>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.TryGetValues("If-None-Match", out var tags) ? tags.Single() : null));
            if (request.RequestUri.AbsolutePath.EndsWith("/0.41.0.json", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (requests.Count > 1)
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            ok.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"s1\"");
            return ok;
        }));
        var updater = new AddonUpdater(client);
        var addon = new ManagedAddon("hoobiscripts", "HoobiScripts", "https://api.example/addons/protected/hoobiscripts/");
        var token = TestContext.Current.CancellationToken;

        var first = await updater.GetSettingsSchemaAsync(addon, "0.42.0", token);
        var second = await updater.GetSettingsSchemaAsync(addon, "0.42.0", token);
        var missing = await updater.GetSettingsSchemaAsync(addon, "0.41.0", token);

        Assert.Equal("https://api.example/addons/protected/hoobiscripts/settings-schema/0.42.0.json", requests[0].Uri);
        Assert.Equal([null, "\"s1\"", null], requests.Select(r => r.IfNoneMatch));
        Assert.Same(first, second);
        Assert.Null(missing);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
