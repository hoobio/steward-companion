using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class CharacterGearTests
{
    private const string Guid = "Player-5826-0A1B2C3D";
    private const string OtherGuid = "Player-5826-11111111";

    private static readonly CharacterObservation Hoobi = new(Guid, "Hoobi", "Nightslayer", "Stormrage", 60, 1, 2, 1, null, null, false, null);
    private static readonly CharacterObservation Grug = new(OtherGuid, "Grug", "Nightslayer", "Stormrage", 58, 7, 3, 2, null, null, false, null);

    private static SavedVariablesSnapshot ReadAccount(string gearTable) =>
        StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, $$"""
            StewardDB = {
            ["gear"] = {
            {{gearTable}}
            },
            }
            """)]);

    private static string GearLua(int schema, string bank = "") => $$"""
        ["{{Guid}}"] = {
            ["schema"] = {{schema}},
            ["observedAt"] = 1790900000,
            ["level"] = 60,
            ["equipped"] = {
                [1] = { ["link"] = "item:12640:1508:0:0:0:0:0:0:60:0:0:0:0", ["itemID"] = 12640, ["quality"] = 4, ["enchantID"] = 1508, ["equipLoc"] = "INVTYPE_HEAD", ["ilvl"] = 63 },
                [16] = { ["link"] = "item:19019:1900:0:0:0:0:0:0:60:0:0:0:0", ["itemID"] = 19019, ["quality"] = 5, ["equipLoc"] = "INVTYPE_WEAPON", ["ilvl"] = 80 },
            },
            ["bags"] = {
                { ["link"] = "item:10247:0:0:0:0:0:1050:0:60:0:0:0:0", ["itemID"] = 10247, ["quality"] = 3, ["suffixID"] = 1050, ["count"] = 1, ["equipLoc"] = "INVTYPE_HEAD", ["ilvl"] = 57 }, -- [1]
            },
            {{bank}}
            ["fp"] = "8bb1ce6a",
        },
        """;

    private const string BankLua = """
        ["bank"] = {
            ["observedAt"] = 1790900000,
            ["items"] = {
                { ["link"] = "item:18813:0:0:0:0:0:0:0:60:0:0:0:0", ["itemID"] = 18813, ["quality"] = 4, ["equipLoc"] = "INVTYPE_FINGER", ["ilvl"] = 71 }, -- [1]
            },
        },
        """;

    private static CharacterGear SampleGear(GearBank? bank = null) => new(
        GearSchema.Current,
        1790900000,
        60,
        new SortedDictionary<string, GearEntry>(StringComparer.Ordinal)
        {
            ["1"] = new("item:12640:1508:0:0:0:0:0:0:60:0:0:0:0", 12640, 4, EnchantId: 1508, EquipLoc: "INVTYPE_HEAD", Ilvl: 63),
            ["16"] = new("item:19019:1900:0:0:0:0:0:0:60:0:0:0:0", 19019, 5),
        },
        [new GearEntry("item:10247:0:0:0:0:0:1050:0:60:0:0:0:0", 10247, 3, SuffixId: 1050, Count: 1, EquipLoc: "INVTYPE_HEAD", Ilvl: 57)],
        bank,
        "8bb1ce6a");

    private static Dictionary<string, CharacterGear> GearFor(CharacterGear gear) => new() { [Guid] = gear };

    [Fact]
    public void Read_MapsSchema1Gear()
    {
        var snapshot = ReadAccount(GearLua(1, BankLua));

        var gear = snapshot.Gear[Guid];
        Assert.Equal(0, snapshot.Skipped);
        Assert.Equal(1790900000, gear.ObservedAt);
        Assert.Equal(60, gear.Level);
        Assert.Equal("8bb1ce6a", gear.Fp);
        Assert.Equal(["1", "16"], gear.Equipped.Keys);
        Assert.Equal(new GearEntry("item:12640:1508:0:0:0:0:0:0:60:0:0:0:0", 12640, 4, 1508, null, null, "INVTYPE_HEAD", 63), gear.Equipped["1"]);
        Assert.Null(gear.Equipped["16"].EnchantId);
        Assert.Equal(new GearEntry("item:10247:0:0:0:0:0:1050:0:60:0:0:0:0", 10247, 3, null, 1050, 1, "INVTYPE_HEAD", 57), Assert.Single(gear.Bags));
        Assert.Equal(1790900000, gear.Bank!.ObservedAt);
        Assert.Equal(18813, Assert.Single(gear.Bank.Items).ItemId);
    }

    [Fact]
    public void Read_MapsAForeverCapture_WithEmptySlotsAsPositionalNils()
    {
        var snapshot = StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, """
            StewardDB = {
            ["gear"] = {
            ["Player-4619-00B33CCD"] = {
            ["fp"] = "057cf2e8",
            ["observedAt"] = 1790948931,
            ["bags"] = {
            },
            ["equipped"] = {
            {
            ["equipLoc"] = "INVTYPE_HEAD",
            ["itemID"] = 4373,
            ["link"] = "item:4373::::::::15:1484::::::::Player-4619-00B33CCD:",
            ["ilvl"] = 24,
            ["quality"] = 2,
            },
            nil,
            nil,
            nil,
            {
            ["equipLoc"] = "INVTYPE_ROBE",
            ["itemID"] = 9598,
            ["link"] = "item:9598::::::::15:1484::11:::::::",
            ["ilvl"] = 10,
            ["quality"] = 2,
            },
            [16] = {
            ["equipLoc"] = "INVTYPE_2HWEAPON",
            ["itemID"] = 15397,
            ["link"] = "item:15397::::::::15:1484::11:::::::",
            ["ilvl"] = 14,
            ["quality"] = 2,
            },
            },
            ["level"] = 15,
            ["schema"] = 1,
            },
            },
            }
            """)]);

        var gear = snapshot.Gear["Player-4619-00B33CCD"];
        Assert.Equal(0, snapshot.Skipped);
        Assert.Equal(["1", "16", "5"], gear.Equipped.Keys);
        Assert.Equal("item:4373::::::::15:1484::::::::Player-4619-00B33CCD:", gear.Equipped["1"].Link);
        Assert.Equal(9598, gear.Equipped["5"].ItemId);
        Assert.Equal(15, gear.Level);
    }

    [Fact]
    public void Read_LeavesBankNull_WhenAbsent()
    {
        var snapshot = ReadAccount(GearLua(1));

        Assert.Null(snapshot.Gear[Guid].Bank);
    }

    [Fact]
    public void Read_RejectsAnotherSchema()
    {
        var snapshot = ReadAccount(GearLua(2, BankLua));

        Assert.Empty(snapshot.Gear);
        Assert.Equal(1, snapshot.Skipped);
    }

    [Fact]
    public void Read_RejectsGear_WhenAnEntryLacksALink()
    {
        var snapshot = ReadAccount(GearLua(1).Replace("[\"link\"] = \"item:19019:1900:0:0:0:0:0:0:60:0:0:0:0\", ", string.Empty, StringComparison.Ordinal));

        Assert.Empty(snapshot.Gear);
        Assert.Equal(1, snapshot.Skipped);
    }

    [Fact]
    public void CharacterSyncEntry_OmitsGear_WhenNoneObserved()
    {
        var entry = CharacterSyncMapping.ToEntry(Grug, new Dictionary<string, CharacterProfessions>(), GearFor(SampleGear()));

        var json = JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry);

        Assert.DoesNotContain("gear", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterSyncEntry_SerialisesGear_AsTheDocumentedPayload()
    {
        var entry = CharacterSyncMapping.ToEntry(Hoobi, new Dictionary<string, CharacterProfessions>(), GearFor(SampleGear()));

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry));
        var gear = doc.RootElement.GetProperty("gear");

        Assert.Equal(1, gear.GetProperty("schema").GetInt32());
        Assert.Equal(1790900000, gear.GetProperty("observedAt").GetInt64());
        Assert.Equal(60, gear.GetProperty("level").GetInt32());
        Assert.Equal("8bb1ce6a", gear.GetProperty("fp").GetString());
        Assert.Equal(JsonValueKind.Null, gear.GetProperty("bank").ValueKind);

        var equipped = gear.GetProperty("equipped");
        Assert.Equal(["1", "16"], equipped.EnumerateObject().Select(p => p.Name));
        var head = equipped.GetProperty("1");
        Assert.Equal("item:12640:1508:0:0:0:0:0:0:60:0:0:0:0", head.GetProperty("link").GetString());
        Assert.Equal(12640, head.GetProperty("itemID").GetInt32());
        Assert.Equal(4, head.GetProperty("quality").GetInt32());
        Assert.Equal(1508, head.GetProperty("enchantID").GetInt32());
        Assert.Equal("INVTYPE_HEAD", head.GetProperty("equipLoc").GetString());
        Assert.Equal(63, head.GetProperty("ilvl").GetInt32());
        Assert.False(head.TryGetProperty("suffixID", out _));
        Assert.False(head.TryGetProperty("count", out _));

        var weapon = equipped.GetProperty("16");
        Assert.False(weapon.TryGetProperty("enchantID", out _));
        Assert.False(weapon.TryGetProperty("equipLoc", out _));
        Assert.False(weapon.TryGetProperty("ilvl", out _));

        var bag = Assert.Single(gear.GetProperty("bags").EnumerateArray());
        Assert.Equal(1050, bag.GetProperty("suffixID").GetInt32());
        Assert.Equal(1, bag.GetProperty("count").GetInt32());
    }

    [Fact]
    public void CharacterSyncEntry_SerialisesTheBank_WhenPresent()
    {
        var bank = new GearBank(1790900000, [new GearEntry("item:18813:0:0:0:0:0:0:0:60:0:0:0:0", 18813, 4, EquipLoc: "INVTYPE_FINGER", Ilvl: 71)]);
        var entry = CharacterSyncMapping.ToEntry(Hoobi, new Dictionary<string, CharacterProfessions>(), GearFor(SampleGear(bank)));

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry));
        var serialisedBank = doc.RootElement.GetProperty("gear").GetProperty("bank");

        Assert.Equal(1790900000, serialisedBank.GetProperty("observedAt").GetInt64());
        Assert.Equal(18813, Assert.Single(serialisedBank.GetProperty("items").EnumerateArray()).GetProperty("itemID").GetInt32());
    }

    [Fact]
    public void Fingerprint_ChangesWhenGearChanges_AndIgnoresObservedAtAndFp()
    {
        var professions = new Dictionary<string, CharacterProfessions>();
        var baseline = CharacterSyncMapping.Fingerprint([Hoobi], professions, gear: GearFor(SampleGear()));
        var restamped = CharacterSyncMapping.Fingerprint([Hoobi], professions, gear: GearFor(SampleGear() with { ObservedAt = 1790999999, Fp = "cd7ea981" }));

        Assert.Equal(baseline, restamped);
        Assert.NotEqual(baseline, CharacterSyncMapping.Fingerprint([Hoobi], professions));
    }

    [Fact]
    public void ShouldPush_WhenOnlyTheGearChanged()
    {
        var professions = new Dictionary<string, CharacterProfessions>();
        var pushed = CharacterSyncMapping.Fingerprint([Hoobi], professions, gear: GearFor(SampleGear()));
        var last = new Dictionary<string, CharacterPushRecord> { ["key"] = new(pushed, DateTimeOffset.UnixEpoch, 1) };

        var unchanged = CharacterSyncMapping.Fingerprint([Hoobi], professions, gear: GearFor(SampleGear()));
        var changed = CharacterSyncMapping.Fingerprint([Hoobi], professions, gear: GearFor(SampleGear() with { Bags = [] }));

        Assert.False(CharacterPushGate.ShouldPush(unchanged, last, "key"));
        Assert.True(CharacterPushGate.ShouldPush(changed, last, "key"));
    }

    [Fact]
    public void Select_SendsTheCharacterWhoseGearChanged()
    {
        var professions = new Dictionary<string, CharacterProfessions>
        {
            [Guid] = new CharacterProfessions(1, null, null, "fp"),
            [OtherGuid] = new CharacterProfessions(1, null, null, "fp"),
        };
        var catalogue = new Dictionary<string, ProfessionCatalogue>();
        var gear = new Dictionary<string, CharacterGear> { [Guid] = SampleGear() };
        var last = new CharacterPushRecord(
            "batch",
            DateTimeOffset.UnixEpoch,
            2,
            Characters: professions.ToDictionary(
                p => p.Key,
                p => new CharacterPushOutcome(true, Fingerprint: CharacterSyncMapping.ProfessionsFingerprint(p.Value, gear.GetValueOrDefault(p.Key)))));

        Assert.False(ProfessionsPushSelection.Select([Hoobi, Grug], professions, catalogue, last, null, null, false, gear).HasWork);

        var changed = new Dictionary<string, CharacterGear> { [Guid] = SampleGear() with { Bags = [] } };
        var plan = ProfessionsPushSelection.Select([Hoobi, Grug], professions, catalogue, last, null, null, false, changed);

        Assert.Equal(Guid, Assert.Single(plan.Characters).CharacterGuid);
    }

    [Fact]
    public void ProfessionsOnlyPath_KeepsGearOnTheKeptCharacters()
    {
        var professions = new Dictionary<string, CharacterProfessions> { [Guid] = new CharacterProfessions(1, null, null, "fp") };
        var gear = GearFor(SampleGear());
        var snapshot = new SavedVariablesSnapshot(
            [], null, [], [], [], 0, [Hoobi, Grug], "irrelevant", professions, new Dictionary<string, ProfessionCatalogue>(), HasAccountData: true)
        {
            Gear = gear,
        };

        var scope = CharacterSyncMapping.Scope(snapshot, professionsOnly: true, allowedGuilds: null);

        var kept = Assert.Single(scope.Characters);
        Assert.Equal(Guid, kept.CharacterGuid);
        Assert.Same(gear[Guid], CharacterSyncMapping.ToEntry(kept, snapshot.Professions, snapshot.Gear).Gear);
        Assert.NotEqual(
            scope.Fingerprint,
            CharacterSyncMapping.Scope(snapshot with { Gear = new Dictionary<string, CharacterGear>() }, professionsOnly: true, allowedGuilds: null).Fingerprint);
    }
}
