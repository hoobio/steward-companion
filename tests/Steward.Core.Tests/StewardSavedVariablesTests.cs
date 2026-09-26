namespace Steward.Core.Tests;

public sealed class StewardSavedVariablesTests : IDisposable
{
    private const string AccountFile = """
        StewardDB = {
        ["exportedAt"] = 1758260000,
        ["roster"] = {
        { ["name"] = "Hoobi", ["realm"] = "Nightslayer", ["class"] = "WARRIOR", ["level"] = 60, ["rank"] = "Officer", ["rankIndex"] = 1, ["note"] = "", ["officerNote"] = "alt of Grug", ["lastOnline"] = 1758250000 }, -- [1]
        { ["name"] = "Grug", ["realm"] = "Nightslayer", ["class"] = "SHAMAN", ["level"] = 58 }, -- [2]
        },
        ["loot"] = {
        { ["id"] = "loot-1", ["at"] = 1758240000, ["player"] = "Hoobi", ["itemId"] = 19019, ["item"] = "Thunderfury", ["quality"] = 5, ["source"] = "Ragnaros", ["instance"] = "Molten Core" }, -- [1]
        },
        ["attendance"] = {
        { ["id"] = "raid-1", ["at"] = 1758230000, ["instance"] = "Molten Core", ["present"] = { "Hoobi", "Grug", }, }, -- [1]
        },
        }
        """;

    private const string CharacterFile = """
        StewardCharDB = {
        ["exportedAt"] = 1758300000,
        ["character"] = { ["name"] = "Grug", ["realm"] = "Nightslayer", ["class"] = "SHAMAN" },
        ["roster"] = {
        { ["name"] = "Ignored", ["realm"] = "Nightslayer" }, -- [1]
        },
        ["loot"] = {
        { ["id"] = "loot-1", ["at"] = 1758240000, ["player"] = "Grug", ["itemId"] = 19019, ["item"] = "Thunderfury", ["quality"] = 5 }, -- [1]
        { ["id"] = "loot-2", ["at"] = 1758290000, ["player"] = "Grug", ["itemId"] = 17182, ["item"] = "Sulfuras", ["quality"] = 5 }, -- [2]
        },
        }
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("steward-sv-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SavedVariablesSnapshot ReadFiles(params (string Path, string Text)[] files) =>
        StewardSavedVariables.Read(files.Select(f => (f.Path, DateTimeOffset.UnixEpoch, f.Text)));

    private string WriteSavedVariables(string scopePath, string text, string fileName = "Steward.lua")
    {
        var directory = Path.Combine(_root, scopePath, "SavedVariables");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Read_MapsTheAccountFile()
    {
        var snapshot = ReadFiles(("account.lua", AccountFile));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758260000), snapshot.ExportedAt);
        Assert.Equal(0, snapshot.Skipped);

        var officer = snapshot.Roster[0];
        Assert.Equal("Hoobi", officer.Name);
        Assert.Equal("Nightslayer", officer.Realm);
        Assert.Equal(60, officer.Level);
        Assert.Equal(1, officer.RankIndex);
        Assert.Equal("alt of Grug", officer.OfficerNote);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758250000), officer.LastOnline);
        Assert.Equal(string.Empty, snapshot.Roster[1].Rank);
        Assert.Null(snapshot.Roster[1].LastOnline);

        var loot = Assert.Single(snapshot.Loot);
        Assert.Equal("Thunderfury", loot.Item);
        Assert.Equal(19019, loot.ItemId);
        Assert.Equal("Ragnaros", loot.Source);

        var raid = Assert.Single(snapshot.Attendance);
        Assert.Equal("Hoobi,Grug", string.Join(',', raid.Present));
    }

    [Fact]
    public void Read_PrefersTheNewestExportForAnOverlappingId()
    {
        var snapshot = ReadFiles(("account.lua", AccountFile), ("character.lua", CharacterFile));

        Assert.Equal(2, snapshot.Loot.Count);
        Assert.Equal("Grug", snapshot.Loot.Single(l => l.Id == "loot-1").Player);
        Assert.Equal("Sulfuras", snapshot.Loot.Single(l => l.Id == "loot-2").Item);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758300000), snapshot.ExportedAt);
    }

    [Fact]
    public void Read_TakesRosterFromTheAccountFileOnly()
    {
        var snapshot = ReadFiles(("account.lua", AccountFile), ("character.lua", CharacterFile));

        Assert.Equal(2, snapshot.Roster.Count);
        Assert.DoesNotContain(snapshot.Roster, member => member.Name == "Ignored");
        Assert.Equal("Grug", Assert.Single(snapshot.Files, f => f.Character is not null).Character);
    }

    [Fact]
    public void Read_YieldsEmptyListsForMissingTables()
    {
        var snapshot = ReadFiles(("account.lua", """StewardDB = { ["exportedAt"] = 1758260000, }"""));

        Assert.Empty(snapshot.Roster);
        Assert.Empty(snapshot.Loot);
        Assert.Empty(snapshot.Attendance);
        Assert.Equal(0, snapshot.Skipped);
    }

    [Fact]
    public void Read_SkipsAndCountsRecordsMissingRequiredFields()
    {
        var snapshot = ReadFiles(("account.lua", """
            StewardDB = {
            ["loot"] = {
            { ["at"] = 1758240000, ["player"] = "Hoobi", ["item"] = "Thunderfury" }, -- [1]
            { ["id"] = "loot-2", ["at"] = 1758290000, ["player"] = "Grug" }, -- [2]
            },
            ["attendance"] = {
            { ["at"] = 1758230000, ["instance"] = "Molten Core" }, -- [1]
            },
            ["roster"] = {
            { ["realm"] = "Nightslayer" }, -- [1]
            },
            }
            """));

        Assert.Equal("loot-2", Assert.Single(snapshot.Loot).Id);
        Assert.Empty(snapshot.Attendance);
        Assert.Empty(snapshot.Roster);
        Assert.Equal(3, snapshot.Skipped);
    }

    [Fact]
    public void Read_YieldsNullExportedAt_WhenAbsent()
    {
        var snapshot = ReadFiles(("account.lua", """StewardDB = { ["loot"] = {}, }"""));

        Assert.Null(snapshot.ExportedAt);
        Assert.Null(Assert.Single(snapshot.Files).ExportedAt);
    }

    [Fact]
    public void FindFiles_ReturnsAccountAndCharacterFiles_IgnoringBackups()
    {
        var account = WriteSavedVariables(Path.Combine("WTF", "Account", "54939295#1"), AccountFile);
        WriteSavedVariables(Path.Combine("WTF", "Account", "54939295#1"), "stale", "Steward.lua.bak");
        WriteSavedVariables(Path.Combine("WTF", "Account", "54939295#1"), "other", "HoobiScripts.lua");
        var grug = WriteSavedVariables(Path.Combine("WTF", "Account", "54939295#1", "Nightslayer", "Grug"), CharacterFile);
        var hoobi = WriteSavedVariables(Path.Combine("WTF", "Account", "54939295#1", "Nightslayer", "Hoobi"), CharacterFile);

        var found = StewardSavedVariables.FindFiles(_root);

        Assert.Equal(new[] { account, grug, hoobi }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase), found);
    }

    [Fact]
    public void Read_MapsCharacters()
    {
        var snapshot = ReadFiles(("account.lua", """
            StewardDB = {
            ["characters"] = {
            ["Player-4395-0A1B2C3D"] = {
                ["name"] = "Hoobi Furry", ["realm"] = "Nightslayer", ["guild"] = "Gigagrug",
                ["level"] = 60, ["classID"] = 1, ["raceID"] = 2, ["rankIndex"] = 1,
                ["lastOnline"] = 1758250000, ["linkedUserId"] = "123456789012345678",
                ["linkKnown"] = true, ["observedAt"] = 1758260000,
            },
            },
            }
            """));

        var character = Assert.Single(snapshot.Characters);
        Assert.Equal("Player-4395-0A1B2C3D", character.CharacterGuid);
        Assert.Equal("Hoobi Furry", character.Name);
        Assert.Equal("Nightslayer", character.Realm);
        Assert.Equal("Gigagrug", character.Guild);
        Assert.Equal(60, character.Level);
        Assert.Equal(1, character.ClassId);
        Assert.Equal(2, character.RaceId);
        Assert.Equal(1, character.RankIndex);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758250000), character.LastOnline);
        Assert.Equal("123456789012345678", character.LinkedUserId);
        Assert.True(character.LinkKnown);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758260000), character.ObservedAt);
        Assert.NotNull(snapshot.CharactersFingerprint);
        Assert.Equal(0, snapshot.Skipped);
    }

    [Fact]
    public void Read_DedupesCharactersByGuid_KeepingTheHighestObservedAt()
    {
        var older = """
            StewardDB = {
            ["characters"] = {
            ["Player-4395-0A1B2C3D"] = { ["name"] = "Hoobi", ["realm"] = "Nightslayer", ["level"] = 55, ["observedAt"] = 1758200000 },
            },
            }
            """;
        var newer = """
            StewardDB = {
            ["characters"] = {
            ["Player-4395-0A1B2C3D"] = { ["name"] = "Hoobi", ["realm"] = "Nightslayer", ["level"] = 60, ["observedAt"] = 1758260000 },
            },
            }
            """;

        var snapshot = ReadFiles(("account-a.lua", older), ("account-b.lua", newer));

        var character = Assert.Single(snapshot.Characters);
        Assert.Equal(60, character.Level);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758260000), character.ObservedAt);
    }

    [Fact]
    public void Read_SkipsACharacterWithNoGuidKey()
    {
        var snapshot = ReadFiles(("account.lua", """
            StewardDB = {
            ["characters"] = {
            { ["name"] = "Orphan", ["realm"] = "Nightslayer" }, -- [1]
            },
            }
            """));

        Assert.Empty(snapshot.Characters);
        Assert.Equal(1, snapshot.Skipped);
    }

    [Fact]
    public void Read_SkipsACharacterMissingRequiredFields()
    {
        var snapshot = ReadFiles(("account.lua", """
            StewardDB = {
            ["characters"] = {
            ["Player-4395-0A1B2C3D"] = { ["realm"] = "Nightslayer" },
            },
            }
            """));

        Assert.Empty(snapshot.Characters);
        Assert.Equal(1, snapshot.Skipped);
    }

    [Fact]
    public void Read_ReturnsNull_WhenNoFileExists()
    {
        Assert.Null(StewardSavedVariables.Read(_root));
    }

    [Fact]
    public void Read_FromDisk_ParsesEveryFound()
    {
        WriteSavedVariables(Path.Combine("WTF", "Account", "54939295#1"), AccountFile);
        WriteSavedVariables(Path.Combine("WTF", "Account", "54939295#1", "Nightslayer", "Grug"), CharacterFile);

        var snapshot = StewardSavedVariables.Read(_root);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.Files.Count);
        Assert.Equal(2, snapshot.Loot.Count);
        Assert.Equal(2, snapshot.Roster.Count);
    }
}
