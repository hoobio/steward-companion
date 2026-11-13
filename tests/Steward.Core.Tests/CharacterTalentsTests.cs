using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class CharacterTalentsTests
{
    private const string Guid = "Player-5826-0A1B2C3D";

    private static readonly CharacterObservation Hoobi = new(Guid, "Hoobi", "Nightslayer", "Stormrage", 60, 1, 2, 1, null, null, false, null);

    private static string TalentsLua(long observedAt, int rank, string fp) => $$"""
        ["{{Guid}}"] = {
            ["schema"] = 1,
            ["observedAt"] = {{observedAt}},
            ["group"] = 1,
            ["tabs"] = { 5, 0, 12 },
            ["nodes"] = {
                { ["id"] = 101, ["rank"] = {{rank}}, ["spell"] = 5570 },
                { ["id"] = 102, ["rank"] = 1, ["spell"] = 1126 },
            },
            ["addonVersion"] = "1.2.3",
            ["fp"] = "{{fp}}",
        },
        """;

    private static SavedVariablesSnapshot Read(string talents) =>
        StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, $$"""
            StewardDB = {
            ["talents"] = {
            {{talents}}
            },
            }
            """)]);

    private static JsonElement EntryJson(SavedVariablesSnapshot snapshot)
    {
        var entry = CharacterSyncMapping.ToEntry(Hoobi, snapshot.Professions, snapshot.Gear, snapshot.Forwarded);
        return JsonDocument.Parse(JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry)).RootElement;
    }

    [Fact]
    public void Talents_RoundTripAsTheSameJsonShape()
    {
        var talents = EntryJson(Read(TalentsLua(1790900000, 2, "abcd1234"))).GetProperty("talents");

        Assert.Equal(1, talents.GetProperty("schema").GetInt32());
        Assert.Equal(1790900000, talents.GetProperty("observedAt").GetInt64());
        Assert.Equal(1, talents.GetProperty("group").GetInt32());
        Assert.Equal("1.2.3", talents.GetProperty("addonVersion").GetString());
        Assert.Equal("abcd1234", talents.GetProperty("fp").GetString());
        Assert.Equal([5, 0, 12], talents.GetProperty("tabs").EnumerateArray().Select(t => t.GetInt32()));
        var nodes = talents.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Equal([101, 102], nodes.Select(n => n.GetProperty("id").GetInt32()));
        Assert.Equal([2, 1], nodes.Select(n => n.GetProperty("rank").GetInt32()));
        Assert.Equal([5570, 1126], nodes.Select(n => n.GetProperty("spell").GetInt32()));
    }

    [Fact]
    public void Talents_AreOmitted_WhenAbsent()
    {
        var snapshot = StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, "StewardDB = { [\"gear\"] = {} }")]);

        Assert.Empty(snapshot.Forwarded);
        Assert.False(EntryJson(snapshot).TryGetProperty("talents", out _));
    }

    [Fact]
    public void Talents_KeepTheNewestObservation_AcrossFiles()
    {
        var snapshot = StewardSavedVariables.Read([
            ("a.lua", DateTimeOffset.UnixEpoch, $$"""StewardDB = { ["talents"] = { {{TalentsLua(1790900100, 3, "new")}} } }"""),
            ("b.lua", DateTimeOffset.UnixEpoch, $$"""StewardDB = { ["talents"] = { {{TalentsLua(1790900000, 1, "old")}} } }"""),
        ]);

        Assert.Equal("new", EntryJson(snapshot).GetProperty("talents").GetProperty("fp").GetString());
    }

    [Fact]
    public void Talents_AreReadFromTheCharacterTable()
    {
        var snapshot = StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, $$"""
            StewardDB = {
            ["characters"] = {
            ["{{Guid}}"] = {
            ["name"] = "Hoobi",
            ["realm"] = "Nightslayer",
            ["talents"] = { ["schema"] = 1, ["observedAt"] = 1790900000, ["tabs"] = { 1, 2, 3 }, ["nodes"] = {} },
            },
            },
            }
            """)]);

        var talents = EntryJson(snapshot).GetProperty("talents");
        Assert.Equal([1, 2, 3], talents.GetProperty("tabs").EnumerateArray().Select(t => t.GetInt32()));
        Assert.Equal(JsonValueKind.Array, talents.GetProperty("nodes").ValueKind);
    }

    [Fact]
    public void Fingerprint_ChangesWhenOnlyTalentsChange_AndIgnoresRestamps()
    {
        var professions = new Dictionary<string, CharacterProfessions>();
        var baseline = Read(TalentsLua(1790900000, 2, "abcd1234")).Forwarded;
        var restamped = Read(TalentsLua(1790999999, 2, "ffff0000")).Forwarded;
        var changed = Read(TalentsLua(1790900000, 3, "abcd1234")).Forwarded;

        var fingerprint = CharacterSyncMapping.Fingerprint([Hoobi], professions, forwarded: baseline);

        Assert.Equal(fingerprint, CharacterSyncMapping.Fingerprint([Hoobi], professions, forwarded: restamped));
        Assert.NotEqual(fingerprint, CharacterSyncMapping.Fingerprint([Hoobi], professions, forwarded: changed));
        Assert.NotEqual(fingerprint, CharacterSyncMapping.Fingerprint([Hoobi], professions));
    }
}
