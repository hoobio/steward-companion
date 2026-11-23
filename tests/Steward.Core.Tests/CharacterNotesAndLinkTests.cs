using System.Text;
using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class CharacterNotesAndLinkTests
{
    private const string Guid = "Player-1234-0000ABCD";

    private const string NoteLua = "\" Alt of \\\"Bob\\\" \\\\ tank|heals ✦\"";

    private const string NoteText = " Alt of \"Bob\" \\ tank|heals ✦";

    private static readonly CharacterObservation Hoobi = new(Guid, "Hoobi", "Nightslayer", "Stormrage", 60, 1, 2, 1, null, null, false, null);

    private static string NotesLua(string note, long observedAt, string fp, string? officerNote = null) => $$"""
        ["{{Guid}}"] = {
            ["note"] = {{note}},
            {{(officerNote is null ? "" : $"[\"officerNote\"] = \"{officerNote}\",")}}
            ["observedAt"] = {{observedAt}},
            ["fp"] = "{{fp}}",
        },
        """;

    private static string LinkSetLua(string userId, long at, string fp, string by = "Hoobi") => $$"""
        ["{{Guid}}"] = {
            ["userId"] = {{userId}},
            ["at"] = {{at}},
            ["observedAt"] = {{at}},
            ["by"] = "{{by}}",
            ["fp"] = "{{fp}}",
        },
        """;

    private static SavedVariablesSnapshot Read(string key, string records) =>
        StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, $$"""StewardDB = { ["{{key}}"] = { {{records}} } }""")]);

    private static JsonElement Record(SavedVariablesSnapshot snapshot, string key)
    {
        var entry = CharacterSyncMapping.ToEntry(Hoobi, snapshot.Professions, snapshot.Gear, snapshot.Forwarded);
        return JsonDocument.Parse(JsonSerializer.Serialize(entry, CompanionJsonContext.Default.CharacterSyncEntry)).RootElement.GetProperty(key);
    }

    private static string Fingerprint(SavedVariablesSnapshot snapshot) =>
        CharacterSyncMapping.Fingerprint([Hoobi], new Dictionary<string, CharacterProfessions>(), forwarded: snapshot.Forwarded);

    [Fact]
    public void Notes_ForwardTheGoldenVectorWithTheNoteByteForByte()
    {
        var notes = Record(Read("notes", NotesLua(NoteLua, 1795000000, "464577f6", "Officer: ok")), "notes");

        Assert.Equal(NoteText, notes.GetProperty("note").GetString());
        Assert.Equal([0x20, 0x41, 0x6C, 0x74, 0x20, 0x6F, 0x66, 0x20, 0x22, 0x42, 0x6F, 0x62, 0x22, 0x20, 0x5C, 0x20, 0x74, 0x61, 0x6E, 0x6B, 0x7C, 0x68, 0x65, 0x61, 0x6C, 0x73, 0x20, 0xE2, 0x9C, 0xA6], Encoding.UTF8.GetBytes(notes.GetProperty("note").GetString()!));
        Assert.Equal("Officer: ok", notes.GetProperty("officerNote").GetString());
        Assert.Equal(1795000000, notes.GetProperty("observedAt").GetInt64());
        Assert.Equal("464577f6", notes.GetProperty("fp").GetString());
        Assert.Equal(
            "{\"note\":\" Alt of \\u0022Bob\\u0022 \\\\ tank|heals \\u2726\",\"officerNote\":\"Officer: ok\",\"observedAt\":1795000000,\"fp\":\"464577f6\"}",
            notes.GetRawText());
    }

    [Fact]
    public void Notes_WithoutAnOfficerNote_OmitTheKey()
    {
        var notes = Record(Read("notes", NotesLua(NoteLua, 1795000000, "e023e7da")), "notes");

        Assert.False(notes.TryGetProperty("officerNote", out _));
        Assert.Equal("e023e7da", notes.GetProperty("fp").GetString());
    }

    [Fact]
    public void Notes_KeepAnEmptyOfficerNote()
    {
        var notes = Record(Read("notes", NotesLua(NoteLua, 1795000000, "3f262cec", "")), "notes");

        Assert.Equal("", notes.GetProperty("officerNote").GetString());
    }

    [Fact]
    public void LinkSet_ForwardsAnUnlinkAsJsonFalse_AndWholeNumbersAsIntegers()
    {
        var linkSet = Record(Read("linkSet", LinkSetLua("false", 1795000000, "cc0794dc")), "linkSet");

        Assert.Equal(
            """{"userId":false,"at":1795000000,"observedAt":1795000000,"by":"Hoobi","fp":"cc0794dc"}""",
            linkSet.GetRawText());
    }

    [Fact]
    public void LinkSet_ForwardsALinkAsTheDiscordIdString()
    {
        var linkSet = Record(Read("linkSet", LinkSetLua("\"300000000000000003\"", 1795000000, "b2748d75")), "linkSet");

        Assert.Equal(
            """{"userId":"300000000000000003","at":1795000000,"observedAt":1795000000,"by":"Hoobi","fp":"b2748d75"}""",
            linkSet.GetRawText());
    }

    [Fact]
    public void LinkSet_KeepsTheLatestAt_AcrossFiles()
    {
        var snapshot = StewardSavedVariables.Read([
            ("a.lua", DateTimeOffset.UnixEpoch, $$"""StewardDB = { ["linkSet"] = { {{LinkSetLua("false", 1795000000, "cc0794dc")}} } }"""),
            ("b.lua", DateTimeOffset.UnixEpoch, $$"""StewardDB = { ["linkSet"] = { {{LinkSetLua("\"300000000000000003\"", 1795000100, "aaaaaaaa")}} } }"""),
            ("c.lua", DateTimeOffset.UnixEpoch, $$"""StewardDB = { ["linkSet"] = { {{LinkSetLua("false", 1794999000, "bbbbbbbb")}} } }"""),
        ]);

        var linkSet = Record(snapshot, "linkSet");
        Assert.Equal("aaaaaaaa", linkSet.GetProperty("fp").GetString());
        Assert.Equal(1795000100, linkSet.GetProperty("at").GetInt64());
    }

    [Fact]
    public void Notes_KeepTheLatestObservation_AcrossFiles()
    {
        var snapshot = StewardSavedVariables.Read([
            ("a.lua", DateTimeOffset.UnixEpoch, $$"""StewardDB = { ["notes"] = { {{NotesLua("\"old\"", 1795000000, "11111111")}} } }"""),
            ("b.lua", DateTimeOffset.UnixEpoch, $$"""StewardDB = { ["notes"] = { {{NotesLua("\"new\"", 1795000500, "22222222")}} } }"""),
        ]);

        Assert.Equal("new", Record(snapshot, "notes").GetProperty("note").GetString());
    }

    [Fact]
    public void Notes_AreReadFromTheCharacterTable()
    {
        var snapshot = StewardSavedVariables.Read([("account.lua", DateTimeOffset.UnixEpoch, $$"""
            StewardDB = {
            ["characters"] = {
            ["{{Guid}}"] = {
            ["name"] = "Hoobi",
            ["realm"] = "Nightslayer",
            ["notes"] = { ["note"] = "main", ["observedAt"] = 1795000000, ["fp"] = "deadbeef" },
            },
            },
            }
            """)]);

        Assert.Equal("main", Record(snapshot, "notes").GetProperty("note").GetString());
    }

    [Fact]
    public void Fingerprint_ChangesWithTheNoteText_TheUserId_AndTheEditTime_ButNotARestamp()
    {
        var note = Fingerprint(Read("notes", NotesLua(NoteLua, 1795000000, "464577f6")));
        Assert.NotEqual(note, Fingerprint(Read("notes", NotesLua("\"other\"", 1795000000, "464577f6"))));
        Assert.NotEqual(note, Fingerprint(Read("notes", NotesLua(NoteLua, 1795000000, "464577f6", "x"))));
        Assert.Equal(note, Fingerprint(Read("notes", NotesLua(NoteLua, 1795009999, "ffffffff"))));

        var link = Fingerprint(Read("linkSet", LinkSetLua("false", 1795000000, "cc0794dc")));
        Assert.NotEqual(link, Fingerprint(Read("linkSet", LinkSetLua("\"300000000000000003\"", 1795000000, "cc0794dc"))));
        Assert.NotEqual(link, Fingerprint(Read("linkSet", LinkSetLua("false", 1795000100, "cc0794dc"))));
    }
}
