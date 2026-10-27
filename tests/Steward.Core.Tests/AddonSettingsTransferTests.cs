using System.Text.Json.Nodes;

namespace Steward.Core.Tests;

public sealed class AddonSettingsTransferTests : IDisposable
{
    private const string ContractVector = "v1.PY2xasNAEAX_5bndQkoiB22ZNOkCdmlcrE-rWES5Pe5WFkbo34NwSDnD8N6CG7gmpGzdFByM2eYeBAnBpriJ5qV9bp_aZleDcJGi3WcE16_7ummqqqoI4SrxSwv4tEC6ziIYH2aX4RjykLyAUIIlBf_PEr71vvHkdtAkQwahz_YD7mUsSnADe550PROizu9XyRJc83ZzJqRRYgEvcBk1-p-V4IPFN3lU6_oL";

    private const string ContractJson = """{"v":1,"product":"wowf","account":"54939295#1","basedOn":1761550000,"changes":[{"addon":"HoobiScripts","scope":"account","key":"autoRepair","from":false,"to":true}],"newCharacters":[],"plans":{"talents":[],"actionBars":[]}}""";

    private const string AccountName = "54939295#1";

    private const string CoreLua = """
        HoobiScriptsDB = {
        ["settings"] = {
        ["autoRepair"] = "all",
        ["hideErrors"] = true,
        ["actionBars"] = 3,
        ["reenableEnemyNameplates"] = "5",
        ["mailTradeGoodsTarget"] = "Grüg",
        ["unknownKey"] = 7,
        },
        ["chars"] = {
        ["Grug Aa-Nightslayer"] = {
        ["class"] = "WARRIOR",
        ["vendorPlainWater"] = false,
        },
        ["Hoobi Drood-Classic Beta PvP"] = {
        ["class"] = "DRUID",
        },
        ["Mage Ling-Nightslayer"] = {
        ["class"] = "MAGE",
        },
        },
        ["talentPlans"] = {
        ["DRUID"] = {
        {
        101,
        102,
        102,
        },
        {
        201,
        },
        },
        ["MAGE"] = {
        [2] = {
        301,
        },
        },
        },
        }
        HoobiScriptsCharDB = nil

        """;

    private const string BarsLua = """
        HoobiScripts_ActionBarsDB = {
        ["plans"] = {
        ["DRUID"] = {
        {
        ["spell"] = 5176,
        },
        nil,
        {
        ["macro"] = "Heal",
        },
        ["macros"] = {
        ["Heal"] = {
        ["perChar"] = false,
        ["variants"] = {
        {
        ["level"] = 1,
        ["body"] = "/cast Healing Touch",
        ["spells"] = {
        "Healing Touch",
        },
        },
        },
        },
        ["PowerShift"] = {
        ["perChar"] = true,
        ["variants"] = {
        },
        },
        },
        },
        ["WARRIOR"] = {
        [13] = {
        ["list"] = {
        {
        ["item"] = 159,
        },
        {
        ["profession"] = 5,
        },
        },
        },
        ["macros"] = {
        },
        },
        },
        ["account"] = {
        [12] = {
        ["spell"] = 1,
        ["fixed"] = true,
        },
        },
        ["classEnabled"] = {
        ["DRUID"] = true,
        },
        ["chars"] = {
        ["Grug Aa-Nightslayer"] = {
        ["applied"] = 1,
        },
        },
        ["stanceNames"] = {
        ["x"] = "y",
        },
        ["applyAccount"] = false,
        }

        """;

    private readonly string _temp = Directory.CreateTempSubdirectory("steward-transfer-").FullName;

    public AddonSettingsTransferTests()
    {
        Toc("HoobiScripts", "Hoobi Scripts", "## SavedVariables: HoobiScriptsDB\n## SavedVariablesPerCharacter: HoobiScriptsCharDB\n");
        Toc("HoobiScripts_ActionBars", "Hoobi Scripts: Action Bars", "## SavedVariables: HoobiScripts_ActionBarsDB\n");
        Toc("StewardJourney", "Steward: Journey", "## SavedVariablesPerCharacter: StewardJourneyDB\n");
        Directory.CreateDirectory(Path.GetDirectoryName(CoreFile)!);
        File.WriteAllText(CoreFile, CoreLua);
        File.WriteAllText(BarsFile, BarsLua);
        Directory.CreateDirectory(Path.Combine(AccountFolder, "91", "Mage-Ling"));
        Directory.CreateDirectory(Path.Combine(AccountFolder, "70", "Hoobi-Drood"));
        var journey = Directory.CreateDirectory(Path.Combine(AccountFolder, "91", "Grug-Aa", "SavedVariables")).FullName;
        File.WriteAllText(Path.Combine(journey, "StewardJourney.lua"), "StewardJourneyDB = {\n[\"settings\"] = {\n[\"enabled\"] = false,\n},\n}\n");
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private string Flavour => Path.Combine(_temp, "_forever_");

    private string AddOns => Path.Combine(Flavour, "Interface", "AddOns");

    private string AccountFolder => AddonSettings.AccountFolder(Flavour, AccountName);

    private string CoreFile => AddonSettings.FilePath(Flavour, AccountName, "HoobiScripts");

    private string BarsFile => AddonSettings.FilePath(Flavour, AccountName, "HoobiScripts_ActionBars");

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static SettingsSchema Schema(string file) => SettingsSchema.Parse(File.ReadAllText(Fixture(file)));

    private static IReadOnlyList<SettingsSource> Sources() =>
    [
        new("hoobiscripts", "HoobiScripts", "0.42.0", Schema("hoobiscripts-settings-schema.json")),
        new("hoobiscripts-actionbars", "HoobiScripts_ActionBars", "0.42.0", Schema(Path.Combine("settings-schemas", "HoobiScripts_ActionBars.json"))),
        new("steward-journey", "StewardJourney", "0.42.0", Schema(Path.Combine("settings-schemas", "StewardJourney.json"))),
    ];

    private void Toc(string folder, string title, string directives)
    {
        var path = Directory.CreateDirectory(Path.Combine(AddOns, folder)).FullName;
        File.WriteAllText(Path.Combine(path, folder + ".toc"), $"## Interface: 16001\n## Title: {title}\n{directives}");
    }

    private SettingsAccount Account(string product = ForeverLaunch.LiveProduct) =>
        new(Flavour, AddOns, AccountName, product, AddonSettings.SavedVariableOwners(AddOns, ["HoobiScripts", "HoobiScripts_ActionBars", "StewardJourney"]));

    private SettingsImport Preview(string body, string product = ForeverLaunch.LiveProduct) =>
        AddonSettingsImport.Preview(AddonSettingsImport.Parse($$"""{"v":1,"product":"{{product}}","account":"{{AccountName}}","basedOn":1761550000,{{body}}}"""), Account(product), Sources());

    private int Apply(SettingsImport import, bool conflicts = false, Func<bool>? running = null) =>
        AddonSettingsImport.Apply(
            import,
            Account(),
            Sources(),
            import.Items.Where(item => item.Status == SettingsImportStatus.Ready || (conflicts && item.Status == SettingsImportStatus.Conflict)),
            running ?? (() => false));

    private static string Change(string key, string from, string to, string addon = "HoobiScripts", string scope = "account", string extra = "") =>
        $$"""{"addon":"{{addon}}","scope":"{{scope}}","key":"{{key}}","from":{{from}},"to":{{to}}{{extra}}}""";

    private LuaValue Settings() => AddonSettings.Load(CoreFile)["HoobiScriptsDB"].GetTable("settings")!;

    private LuaValue Bars() => AddonSettings.Load(BarsFile)["HoobiScripts_ActionBarsDB"];

    [Fact]
    public void Payload_DecodesTheContractVector() => Assert.Equal(ContractJson, AddonSettingsPayload.Decode(ContractVector));

    [Fact]
    public void Payload_RoundTripsAndReadsEveryLinkForm()
    {
        var payload = AddonSettingsPayload.Encode(ContractJson);

        Assert.StartsWith("v1.", payload, StringComparison.Ordinal);
        Assert.DoesNotContain('=', payload);
        Assert.Equal(ContractJson, AddonSettingsPayload.Decode(payload));
        Assert.Equal(payload, AddonSettingsPayload.FromLink($"steward://import#{payload}"));
        Assert.Equal(payload, AddonSettingsPayload.FromLink($"  {payload}\n"));
        Assert.Equal(payload, AddonSettingsPayload.FromLink($"steward://import#{payload.Replace(".", "%2E", StringComparison.Ordinal)}"));
        Assert.Throws<FormatException>(() => AddonSettingsPayload.FromLink("steward://other#" + payload));
        Assert.Throws<FormatException>(() => AddonSettingsPayload.FromLink("https://guild.hoobi.io"));
        Assert.Throws<FormatException>(() => AddonSettingsPayload.FromLink("v1." + new string('A', AddonSettingsPayload.MaxLinkLength)));
        Assert.Throws<FormatException>(() => AddonSettingsPayload.Decode("v1.!!"));
        Assert.Throws<FormatException>(() => AddonSettingsImport.Parse(AddonSettingsPayload.Decode(AddonSettingsPayload.Encode("""{"v":2,"product":"wowf","account":"a"}"""))));
        Assert.Throws<FormatException>(() => AddonSettingsImport.Parse("""{"v":1,"account":"a"}"""));
    }

    [Fact]
    public void ExportUrl_PutsTheViewInTheQueryAndThePayloadInTheFragment()
    {
        Assert.Equal("https://guild.hoobi.io/addon-settings#v1.x", AddonSettingsPayload.ExportUrl("v1.x"));
        Assert.Equal("https://guild.hoobi.io/addon-settings?view=talents#v1.x", AddonSettingsPayload.ExportUrl("v1.x", "talents"));
    }

    [Fact]
    public void Export_MatchesTheCheckedInVector()
    {
        var json = AddonSettingsExport.Build(Account(), Sources(), "World of Warcraft: Forever", 1761550000);
        var payload = AddonSettingsPayload.Encode(json);
        var expected = File.ReadAllText(Fixture("addon-settings-export-vector.json")).Trim();

        Assert.True(expected == json, json + "\n" + payload);
        Assert.Equal(expected, AddonSettingsPayload.Decode(File.ReadAllText(Fixture("addon-settings-export-vector.txt")).Trim()));
    }

    [Fact]
    public void Export_HoldsOnlyKeysInTheFileAndLeavesOutBetaCharactersOnTheLiveClient()
    {
        var live = JsonNode.Parse(AddonSettingsExport.Build(Account(), Sources(), "Forever", 1))!;
        var beta = JsonNode.Parse(AddonSettingsExport.Build(Account(ForeverLaunch.BetaProduct), Sources(), "Beta", 1))!;

        var account = live["values"]!["HoobiScripts"]!["account"]!.AsObject();
        Assert.Equal(["autoRepair", "hideErrors", "reenableEnemyNameplates", "actionBars", "mailTradeGoodsTarget"], account.Select(entry => entry.Key));
        Assert.Equal("Grüg", (string?)account["mailTradeGoodsTarget"]);
        Assert.False((bool)live["values"]!["HoobiScripts"]!["character"]!["Grug Aa-Nightslayer"]!["vendorPlainWater"]!);
        Assert.Null(live["values"]!["HoobiScripts"]!["character"]!["Mage Ling-Nightslayer"]);
        Assert.True((bool)live["values"]!["HoobiScripts_ActionBars"]!["class"]!["DRUID"]!["{class}"]!);
        Assert.False((bool)live["values"]!["StewardJourney"]!["characterFile"]!["Grug Aa-Nightslayer"]!["enabled"]!);
        Assert.Equal(["Grug Aa-Nightslayer", "Mage Ling-Nightslayer"], live["characters"]!.AsArray().Select(c => (string?)c!["key"]));
        Assert.Equal(["Nightslayer"], live["realms"]!.AsArray().Select(r => (string?)r!["name"]));
        Assert.Contains("Hoobi Drood-Classic Beta PvP", beta["characters"]!.AsArray().Select(c => (string?)c!["key"]));
        Assert.Contains("70", beta["realms"]!.AsArray().Select(r => (string?)r!["id"]));
    }

    [Fact]
    public void Export_ReadsPositionalAndKeyedPlans()
    {
        var plans = JsonNode.Parse(AddonSettingsExport.Build(Account(), Sources(), "Forever", 1))!["plans"]!;

        Assert.Equal("""{"DRUID":{"1":[101,102,102],"2":[201]},"MAGE":{"2":[301]}}""", plans["talents"]!.ToJsonString());
        var druid = plans["actionBars"]!["classes"]!["DRUID"]!;
        Assert.Equal(5176, (int)druid["1"]!["spell"]!);
        Assert.Null(druid["2"]);
        Assert.Equal("Heal", (string?)druid["3"]!["macro"]);
        Assert.Equal("""["Healing Touch"]""", druid["macros"]!["Heal"]!["variants"]![0]!["spells"]!.ToJsonString());
        Assert.Equal("""{"list":[{"item":159},{"profession":5}]}""", plans["actionBars"]!["classes"]!["WARRIOR"]!["13"]!.ToJsonString());
        Assert.True((bool)plans["actionBars"]!["account"]!["12"]!["fixed"]!);
    }

    [Fact]
    public void Export_MarksARefusedSchemaUnavailable()
    {
        var json = JsonNode.Parse(AddonSettingsExport.Build(Account(), [new("hoobiscripts", "HoobiScripts", "0.42.0", null)], "Forever", 1))!;

        Assert.True((bool)json["addons"]![0]!["unavailable"]!);
        Assert.Empty(json["values"]!.AsObject());
    }

    [Fact]
    public void Import_WritesAChangeAndKeepsEverythingElse()
    {
        var import = Preview($$"""
            "changes":[{{Change("hideErrors", "true", "false")}},{{Change("autoRepair", "\"all\"", "\"own\"")}}]
            """);

        Assert.All(import.Items, item => Assert.Equal(SettingsImportStatus.Ready, item.Status));
        var hide = import.Items[0];
        Assert.Equal(("Hoobi Scripts", "General", "Hide error messages", "On", "Off"), (hide.Addon, hide.Page, hide.Label, hide.Old, hide.New));
        Assert.Equal(("Guild gold, then own", "Own gold"), (import.Items[1].Old, import.Items[1].New));
        Assert.Equal(2, Apply(import));

        var settings = Settings();
        Assert.Null(settings.Get("hideErrors"));
        Assert.Equal("own", settings.GetString("autoRepair"));
        Assert.Equal(7, settings.GetNumber("unknownKey"));
        Assert.False(settings.Get("questMobSparkles")!.Boolean);
        Assert.Equal("Grüg", AddonSettings.FromFile(settings.GetString("mailTradeGoodsTarget")!));
        Assert.Equal(3, AddonSettings.Characters(AddonSettings.Load(CoreFile)).Count);
    }

    [Fact]
    public void Import_WritesAConflictOnlyWhenTicked()
    {
        var before = File.ReadAllText(CoreFile);
        var import = Preview($$"""
            "changes":[{{Change("hideErrors", "false", "false")}},{{Change("skin", "null", "\"hoobi\"")}}]
            """);

        Assert.Equal(SettingsImportStatus.Conflict, import.Items[0].Status);
        Assert.Equal(SettingsImportItem.ChangedInGame, import.Items[0].Reason);
        Assert.Equal(SettingsImportStatus.Ready, import.Items[1].Status);
        AddonSettingsImport.Apply(import, Account(), Sources(), [], () => false);
        Assert.Equal(before, File.ReadAllText(CoreFile));

        Assert.Equal(2, Apply(import, conflicts: true));
        Assert.Null(Settings().Get("hideErrors"));
        Assert.Equal("hoobi", Settings().GetString("skin"));
    }

    [Theory]
    [InlineData("HoobiScripts", "autoRepair", "\"free\"", "Not one of the choices")]
    [InlineData("HoobiScripts", "hideErrors", "\"yes\"", "Must be on or off")]
    [InlineData("HoobiScripts", "actionBars", "16", "Not a valid set of options")]
    [InlineData("HoobiScripts", "actionBars", "1.5", "Not a valid set of options")]
    [InlineData("HoobiScripts", "reenableEnemyNameplates", "\"9999\"", "Enter a whole number from 1 to 3600")]
    [InlineData("HoobiScripts", "reenableEnemyNameplates", "\"1234567\"", "Use 6 characters or fewer")]
    [InlineData("HoobiScripts", "autoSellLimit", "-1", "Must be from 0 to 9,999,999 gold")]
    [InlineData("HoobiScripts", "autoSellLimit", "100000000000", "Must be from 0 to 9,999,999 gold")]
    [InlineData("HoobiScripts", "mailTradeGoodsTarget", "\"Bob<script>\"", "Enter a character name")]
    [InlineData("HoobiScripts", "nope", "true", "Not a setting in the installed version")]
    [InlineData("HoobiScripts", "hideErrors", "{\"a\":1}", "Must be on or off")]
    [InlineData("Missing", "hideErrors", "true", "Steward has no settings for this addon on this install")]
    public void Import_RefusesAValueTheSchemaDoesNotAllow(string addon, string key, string to, string reason)
    {
        var before = File.ReadAllText(CoreFile);
        var import = Preview($$"""
            "changes":[{{Change(key, "null", to, addon)}}]
            """);

        var item = Assert.Single(import.Items);
        Assert.Equal((SettingsImportStatus.Refused, reason), (item.Status, item.Reason));
        Assert.Equal(0, Apply(import, conflicts: true));
        Assert.Equal(before, File.ReadAllText(CoreFile));
    }

    [Fact]
    public void Import_RefusesLockedUnownedAndUnwrittenRows()
    {
        Toc("Details", "Details", "");
        File.Delete(BarsFile);
        var import = Preview($$"""
            "changes":[
              {{Change("threatMeter", "true", "false")}},
              {{Change("applyAccount", "false", "true", "HoobiScripts_ActionBars")}},
              {{Change("enabled", "null", "false", "StewardJourney", "characterFile", ",\"character\":\"Mage Ling-Nightslayer\"")}},
              {{Change("enabled", "null", "false", "StewardJourney", "characterFile", ",\"character\":\"Nobody Here-Nightslayer\"")}}
            ]
            """);

        Assert.Equal(
            ["Off while Details is installed", AddonSettings.NotLoggedInNotice("Hoobi Scripts: Action Bars"), AddonSettings.NotLoggedInNotice("Steward: Journey"), "Log in to this character once first"],
            import.Items.Select(item => item.Reason));
        Assert.Equal(0, Apply(import, conflicts: true));
        Assert.False(File.Exists(BarsFile));

        File.Delete(Path.Combine(AddOns, "HoobiScripts_ActionBars", "HoobiScripts_ActionBars.toc"));
        Assert.Equal(SettingsAccount.NoOwnerNotice, Preview($$"""
            "changes":[{{Change("applyAccount", "false", "true", "HoobiScripts_ActionBars")}}]
            """).Items[0].Reason);
    }

    [Fact]
    public void Import_WritesNothingWhileTheClientRuns()
    {
        var before = File.ReadAllText(CoreFile);
        var import = Preview($$"""
            "newCharacters":[{"first":"zug","last":"zug","class":"WARRIOR","realm":"Nightslayer"}],
            "changes":[{{Change("hideErrors", "true", "false")}}]
            """);

        Assert.Throws<InvalidOperationException>(() => Apply(import, running: () => true));
        Assert.Equal(before, File.ReadAllText(CoreFile));
        Assert.False(Directory.Exists(Path.Combine(AccountFolder, "91", "Zug-Zug")));

        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => Apply(import, running: () => ++calls > 1));
        Assert.Equal(before, File.ReadAllText(CoreFile));
    }

    [Fact]
    public void Import_AddsANewCharacterOnAKnownRealmWithItsSettings()
    {
        var import = Preview($$"""
            "newCharacters":[{"first":"zug","last":"o'zug","class":"WARRIOR","realm":"nightslayer"}],
            "changes":[{{Change("vendorPlainWater", "null", "false", scope: "character", extra: ",\"character\":\"Zug O'zug-Nightslayer\"")}}]
            """);

        Assert.All(import.Items, item => Assert.Equal(SettingsImportStatus.Ready, item.Status));
        Assert.Equal("Zug O'zug (Nightslayer)", import.Items[0].Label);
        Assert.Equal(2, Apply(import));

        Assert.True(Directory.Exists(Path.Combine(AccountFolder, "91", "Zug-O'zug")));
        var entry = AddonSettings.Load(CoreFile)["HoobiScriptsDB"].GetTable("chars")!.GetTable("Zug O'zug-Nightslayer")!;
        Assert.Equal("WARRIOR", entry.GetString("class"));
        Assert.False(entry.Get("vendorPlainWater")!.Boolean);
    }

    [Fact]
    public void Import_RefusesANewCharacterItCannotPlace()
    {
        var import = Preview($$"""
            "newCharacters":[
              {"first":"Jean-Luc","last":"Aa","class":"WARRIOR","realm":"Nightslayer"},
              {"first":"Zug","last":"Zug","class":"WARRIOR","realm":"Classic Beta PvP"},
              {"first":"grug","last":"aa","class":"WARRIOR","realm":"Nightslayer"},
              {"first":"Zug","last":"Zug","class":"DEATHKNIGHT","realm":"Nightslayer"}
            ],
            "changes":[{{Change("vendorPlainWater", "null", "false", scope: "character", extra: ",\"character\":\"Zug Zug-Nightslayer\"")}}]
            """);

        Assert.Equal(
            ["First name: Use letters and apostrophes only", "Log in to a character on this realm once first", "This character already has settings", "Not a class", "Steward does not know this character"],
            import.Items.Select(item => item.Reason));
    }

    [Fact]
    public void Import_WritesAClassRowUnderTheClassOfItsCharacter()
    {
        var import = Preview($$"""
            "changes":[
              {{Change("{class}", "null", "false", "HoobiScripts_ActionBars", "class", ",\"character\":\"Grug Aa-Nightslayer\"")}},
              {{Change("{class}", "true", "false", "HoobiScripts_ActionBars", "class", ",\"character\":\"Hoobi Drood-Classic Beta PvP\"")}},
              {{Change("{class}", "null", "false", "HoobiScripts_ActionBars", "class")}}
            ]
            """);

        Assert.Equal([SettingsImportStatus.Ready, SettingsImportStatus.Ready, SettingsImportStatus.Refused], import.Items.Select(item => item.Status));
        Assert.Equal("Apply this class's plan (Warrior)", import.Items[0].Label);
        Assert.Equal("Names no character", import.Items[2].Reason);
        Apply(import);
        Assert.False(Bars().GetTable("classEnabled")!.Get("WARRIOR")!.Boolean);
        Assert.False(Bars().GetTable("classEnabled")!.Get("DRUID")!.Boolean);
    }

    [Fact]
    public void Import_DecodesTheWebsitesVector()
    {
        var json = AddonSettingsPayload.Decode(File.ReadAllText(Fixture("addon-settings-import-vector.txt")).Trim());
        Assert.Equal(File.ReadAllText(Fixture("addon-settings-import-vector.json")), json);

        var payload = AddonSettingsImport.Parse(json);
        Assert.Equal((ForeverLaunch.LiveProduct, AccountName), (payload.Product, payload.Account));
        var import = AddonSettingsImport.Preview(payload, Account(), Sources());
        Assert.Equal(8, import.Items.Count);
        Assert.Equal("Log in to a character on this realm once first", import.Items[0].Reason);
        Assert.Equal(("Hoobi Scripts", "Auto repair", "Guild gold, then own", "Guild gold, then own"), (import.Items[1].Addon, import.Items[1].Label, import.Items[1].Old, import.Items[1].New));
        Assert.Equal(SettingsImportStatus.Conflict, import.Items[1].Status);
        Assert.Equal(SettingsImportStatus.Conflict, import.Items[6].Status);
    }

    [Fact]
    public void Import_ReplacesOneTalentPlan()
    {
        var import = Preview("""
            "changes":[],"plans":{"talents":[
              {"class":"DRUID","group":1,"from":[101,102,102],"to":[101,101]},
              {"class":"MAGE","group":2,"from":[1],"to":[7]},
              {"class":"DRUID","group":3,"from":null,"to":[1]},
              {"class":"FOO","group":1,"from":null,"to":[1]},
              {"class":"MAGE","group":1,"from":null,"to":[0]},
              {"class":"MAGE","group":1,"from":null,"to":[1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1]}
            ],"actionBars":[]}
            """);

        Assert.Equal(("3 points", "2 points", SettingsImportStatus.Ready), (import.Items[0].Old, import.Items[0].New, import.Items[0].Status));
        Assert.Equal(SettingsImportStatus.Conflict, import.Items[1].Status);
        Assert.Equal(
            ["The talent group is not 1 or 2", "Not a class", "Talent ids must be positive whole numbers", "More than 51 talent points"],
            import.Items.Skip(2).Select(item => item.Reason));
        Assert.Equal(1, Apply(import));

        var plans = AddonSettings.Load(CoreFile)["HoobiScriptsDB"].GetTable("talentPlans")!;
        Assert.Equal([101L, 101L], SettingsPlans.Talents(SettingsPlans.GetIndex(plans.GetTable("DRUID"), 1)!));
        Assert.Equal([201L], SettingsPlans.Talents(SettingsPlans.GetIndex(plans.GetTable("DRUID"), 2)!));
        Assert.Equal([301L], SettingsPlans.Talents(SettingsPlans.GetIndex(plans.GetTable("MAGE"), 2)!));
    }

    [Fact]
    public void Import_ReplacesOneActionBarPlanAndKeepsTheRest()
    {
        var exported = JsonNode.Parse(AddonSettingsExport.Build(Account(), Sources(), "Forever", 1))!["plans"]!["actionBars"]!;
        var from = exported["classes"]!["DRUID"]!.ToJsonString();
        const string To = """{"1":{"spell":5185,"fixed":true},"2":{"macro":"Heal"},"14":{"macro":"PowerShift"},"macros":{"Heal":{"perChar":true,"variants":[{"level":1,"body":"/cast Regrowth","spells":["Regrowth"]}]}}}""";
        var import = Preview($$$"""
            "changes":[],"plans":{"talents":[],"actionBars":[
              {"target":"DRUID","from":{{{from}}},"to":{{{To}}}},
              {"target":"account","from":{"12":{"spell":2}},"to":{}}
            ]}
            """);

        Assert.Equal(SettingsImportStatus.Ready, import.Items[0].Status);
        Assert.Equal(("2 slots, 2 macros", "3 slots, 1 macro"), (import.Items[0].Old, import.Items[0].New));
        Assert.Equal(SettingsImportStatus.Conflict, import.Items[1].Status);
        var warriorBefore = SettingsPlans.Canonical(SettingsPlans.PlanToJson(Bars().GetTable("plans")!.GetTable("WARRIOR")!));
        Assert.Equal(1, Apply(import));

        var bars = Bars();
        var druid = bars.GetTable("plans")!.GetTable("DRUID")!;
        Assert.Equal(5185, SettingsPlans.GetIndex(druid, 1)!.GetNumber("spell"));
        Assert.Null(SettingsPlans.GetIndex(druid, 3));
        Assert.Equal("/cast Regrowth", druid.GetTable("macros")!.GetTable("Heal")!.GetTable("variants")!.Items[0].GetString("body"));
        Assert.NotNull(druid.GetTable("macros")!.GetTable("PowerShift"));
        Assert.Equal(warriorBefore, SettingsPlans.Canonical(SettingsPlans.PlanToJson(bars.GetTable("plans")!.GetTable("WARRIOR")!)));
        Assert.True(bars.GetTable("classEnabled")!.Get("DRUID")!.Boolean);
        Assert.Equal(1, bars.GetTable("chars")!.GetTable("Grug Aa-Nightslayer")!.GetNumber("applied"));
        Assert.Equal("y", bars.GetTable("stanceNames")!.GetString("x"));
        Assert.False(bars.Get("applyAccount")!.Boolean);
        Assert.Single(bars.GetTable("account")!.Table);
        Assert.Contains("[1] = {", File.ReadAllText(BarsFile), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"181":{"spell":1}}""", "181 is not a slot from 1 to 180")]
    [InlineData("""{"x":{"spell":1}}""", "x is not a slot from 1 to 180")]
    [InlineData("""{"1":{"macro":"Nope"}}""", "Slot 1: the macro Nope is not defined")]
    [InlineData("""{"1":{"spell":1,"item":2}}""", "Slot 1: needs exactly one of spell, macro, item, profession or list")]
    [InlineData("""{"1":{"item":2,"fixed":true}}""", "Slot 1: unknown field fixed")]
    [InlineData("""{"1":{"spell":-4}}""", "Slot 1: the spell is not a spell id")]
    [InlineData("""{"1":{"profession":4}}""", "Slot 1: the profession is not 1, 2, 3 or 5")]
    [InlineData("""{"1":{"list":[{"list":[{"spell":1}]}]}}""", "Slot 1: the list is not a list of entries")]
    [InlineData("""{"macros":{"PowerShift":{"perChar":true,"variants":[{"level":1,"body":""}]}}}""", "Macro PowerShift: the action bars addon generates this macro, so a plan cannot define it")]
    [InlineData("""{"macros":{"M":{"perChar":true,"variants":[{"level":0,"body":""}]}}}""", "Macro M: a variant's level is not a positive whole number")]
    [InlineData("""{"macros":{"M":{"perChar":"yes","variants":[{"level":1,"body":""}]}}}""", "Macro M: perChar is not true or false")]
    [InlineData("""{"macros":{"M":{"perChar":true,"variants":[{"level":1,"body":"","spells":[1]}]}}}""", "Macro M: a variant's spells are not spell names")]
    public void Import_RefusesAnInvalidActionBarPlan(string plan, string reason)
    {
        var import = Preview($$"""
            "changes":[],"plans":{"talents":[],"actionBars":[{"target":"WARRIOR","from":null,"to":{{plan}}}]}
            """);

        Assert.Equal(reason, Assert.Single(import.Items).Reason);
    }

    [Fact]
    public void Import_RefusesAMacroBodyOver255Characters()
    {
        var plan = """{"macros":{"M":{"perChar":true,"variants":[{"level":1,"body":"BODY"}]}}}""".Replace("BODY", new string('a', 256), StringComparison.Ordinal);
        var import = Preview($$"""
            "changes":[],"plans":{"talents":[],"actionBars":[{"target":"account","from":null,"to":{{plan}}}]}
            """);

        Assert.Equal("Macro M: a variant's body is missing or longer than 255 characters", Assert.Single(import.Items).Reason);
    }
}
