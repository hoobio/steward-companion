namespace Steward.Core.Tests;

public sealed class StewardSyncFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("steward-sync-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SyncPayload SamplePayload() => new(
        DateTimeOffset.FromUnixTimeSeconds(1758270000),
        DateTimeOffset.FromUnixTimeSeconds(1758260000),
        [],
        [
            new LootEvent("loot-1", DateTimeOffset.FromUnixTimeSeconds(1758240000), "Hoobi", 19019, "Thunderfury", 5, "Ragnaros", "Molten Core"),
            new LootEvent("loot-2", DateTimeOffset.FromUnixTimeSeconds(1758241000), "Grug", 17182, "Sulfuras", 5, null, null),
        ],
        [
            new AttendanceRecord("raid-1", DateTimeOffset.FromUnixTimeSeconds(1758230000), "Molten Core", ["Hoobi", "Grug"]),
        ],
        [],
        [],
        []);

    private static SyncPayload SamplePayloadWithGuildData() => SamplePayload() with
    {
        Members =
        [
            new GuildRosterMember(
                "111", "Hoobi", null, "hoobi#0001", "Raider", ["EU"], ["core"], null,
                "Reliable", false, 12, 1758200000,
                new GuildBuild("WARRIOR", "Fury", "Melee"), null),
        ],
        Discord =
        [
            new DiscordMember("222", "Grug", null),
        ],
    };

    private static AvatarImage SampleAvatar() =>
        new("https://cdn.discordapp.com/avatars/1/hash.png?size=64", 64, 64, new byte[64 * 64 * 4]);

    private string InstallAddon(bool withToc = true)
    {
        var addOnsPath = Path.Combine(_root, "AddOns");
        var addonPath = Path.Combine(addOnsPath, "Steward");
        Directory.CreateDirectory(addonPath);
        if (withToc)
        {
            File.WriteAllText(Path.Combine(addonPath, "Steward.toc"), "## Interface: 11507\n");
        }

        return addOnsPath;
    }

    [Fact]
    public void Render_StartsWithTheLoadSyncCall()
    {
        var rendered = StewardSyncFile.Render(SamplePayload());

        Assert.StartsWith("Steward.LoadSync(", rendered, StringComparison.Ordinal);
        Assert.EndsWith(")" + Environment.NewLine, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ProducesAParsableTable()
    {
        var rendered = StewardSyncFile.Render(SamplePayload());
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        Assert.Equal(1758270000d, table.GetNumber("writtenAt"));
        Assert.Equal(1758260000d, table.GetNumber("exportedAt"));

        var loot = table.GetTable("loot")!.Items;
        Assert.Equal(2, loot.Count);
        Assert.Equal("Thunderfury", loot[0].GetString("item"));
        Assert.Equal("Ragnaros", loot[0].GetString("source"));
        Assert.Null(loot[1].GetString("source"));

        var attendance = Assert.Single(table.GetTable("attendance")!.Items);
        Assert.Equal("Molten Core", attendance.GetString("instance"));
        Assert.Equal(["Hoobi", "Grug"], attendance.GetTable("present")!.Items.Select(i => i.Text));

        Assert.Empty(table.GetTable("members")!.Items);
        Assert.Empty(table.GetTable("discord")!.Items);
        Assert.Empty(table.GetTable("statuses")!.Items);
    }

    [Fact]
    public void Render_ProducesTheStatusesInOrder()
    {
        var payload = SamplePayload() with { Statuses = ["Officer", "Raider", "Approved", "Trial", "Declined"] };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        Assert.Equal(
            ["Officer", "Raider", "Approved", "Trial", "Declined"],
            table.GetTable("statuses")!.Items.Select(i => i.Text));
    }

    [Fact]
    public void Fingerprint_IsTheSha256OfTheRenderedUtf8Text()
    {
        var payload = SamplePayload() with { Statuses = [.. Enumerable.Range(0, 2000).Select(i => $"Raider\U0001F600é{i}")] };
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            StewardSyncFile.Render(payload with { WrittenAt = DateTimeOffset.UnixEpoch }) + payload.Avatar?.SourceUrl)));

        Assert.Equal(expected, StewardSyncFile.Fingerprint(payload));
    }

    [Fact]
    public void Fingerprint_Changes_WhenTheStatusesAreReordered()
    {
        var payload = SamplePayload() with { Statuses = ["Officer", "Raider"] };
        var reordered = payload with { Statuses = ["Raider", "Officer"] };

        Assert.NotEqual(StewardSyncFile.Fingerprint(payload), StewardSyncFile.Fingerprint(reordered));
    }

    [Fact]
    public void Render_ProducesMembersAndDiscordTables()
    {
        var rendered = StewardSyncFile.Render(SamplePayloadWithGuildData());
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        var member = Assert.Single(table.GetTable("members")!.Items);
        Assert.Equal("111", member.GetString("userId"));
        Assert.Equal("Hoobi", member.GetString("name"));
        Assert.Null(member.GetString("displayName"));
        Assert.Equal("hoobi#0001", member.GetString("discordTag"));
        Assert.Equal(["EU"], member.GetTable("origin")!.Items.Select(i => i.Text));
        Assert.Equal("WARRIOR", member.GetTable("primary")!.GetString("class"));
        Assert.Null(member.GetTable("secondary"));
        Assert.Null(member.GetTable("main"));

        var discord = Assert.Single(table.GetTable("discord")!.Items);
        Assert.Equal("222", discord.GetString("id"));
        Assert.Equal("Grug", discord.GetString("name"));
        Assert.Null(discord.GetString("nick"));
    }

    [Fact]
    public void Render_ProducesTheMainTable_WhenTheMemberCarriesOne()
    {
        var payload = SamplePayloadWithGuildData() with
        {
            Members =
            [
                new GuildRosterMember(
                    "111", "Hoobi", null, "hoobi#0001", "Raider", ["EU"], ["core"], null,
                    "Reliable", false, 12, 1758200000,
                    new GuildBuild("WARRIOR", "Fury", "Melee"), null,
                    new GuildMain("Player-4619-00B33CCD", "Hoobi Furry", 60, 1)),
            ],
        };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        var main = Assert.Single(table.GetTable("members")!.Items).GetTable("main")!;
        Assert.Equal("Player-4619-00B33CCD", main.GetString("guid"));
        Assert.Equal("Hoobi Furry", main.GetString("name"));
        Assert.Equal(60d, main.GetNumber("level"));
        Assert.Equal(1d, main.GetNumber("classID"));
    }

    [Fact]
    public void Render_ProducesTheOriginsTable()
    {
        var payload = SamplePayload() with { Origins = [new OriginDef("EU", "#1d7fd6")] };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        var origin = Assert.Single(table.GetTable("origins")!.Items);
        Assert.Equal("EU", origin.GetString("name"));
        Assert.Equal("#1d7fd6", origin.GetString("color"));
    }

    [Fact]
    public void Render_ProducesAnEmptyOriginsTable_WhenThePayloadCarriesNone()
    {
        var rendered = StewardSyncFile.Render(SamplePayload());
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        Assert.Empty(table.GetTable("origins")!.Items);
    }

    [Fact]
    public void Render_OmitsTheCatalogueKey_WhenThePayloadCarriesNone()
    {
        var rendered = StewardSyncFile.Render(SamplePayload());

        Assert.DoesNotContain("catalogue", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ProducesTheCatalogueTable()
    {
        var payload = SamplePayload() with
        {
            Catalogue = new Dictionary<string, IReadOnlyList<CatalogueRecipe>>
            {
                ["Alchemy"] =
                [
                    new CatalogueRecipe("Major Healing Potion", 11460, "Potions", 13446, string.Empty,
                        [new ProfessionReagent("Golden Sansam", 13464, 2)], Order: 3, Grey: 320, YellowFrom: 290),
                ],
            },
        };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        var recipe = Assert.Single(table.GetTable("catalogue")!.GetTable("Alchemy")!.Items);
        Assert.Equal(11460d, recipe.GetNumber("recipeId"));
        Assert.Equal("Major Healing Potion", recipe.GetString("name"));
        Assert.Equal("Potions", recipe.GetString("header"));
        Assert.Equal(13446d, recipe.GetNumber("itemId"));
        Assert.Equal(string.Empty, recipe.GetString("tools"));
        Assert.Equal<(double?, double?, double?)>((3d, 320d, 290d), (recipe.GetNumber("order"), recipe.GetNumber("grey"), recipe.GetNumber("yellowFrom")));
        Assert.Null(recipe.GetNumber("greenFrom"));

        var reagent = Assert.Single(recipe.GetTable("reagents")!.Items);
        Assert.Equal(13464d, reagent.GetNumber("itemId"));
        Assert.Equal("Golden Sansam", reagent.GetString("name"));
        Assert.Equal(2d, reagent.GetNumber("count"));
    }

    [Fact]
    public void Render_OmitsTheMeKey_WhenThePayloadCarriesNone()
    {
        var rendered = StewardSyncFile.Render(SamplePayload());

        Assert.DoesNotContain("\"me\"", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ProducesTheMeTable()
    {
        var payload = SamplePayload() with { Me = new SyncMe("123456789012345678", "admin", ["steward", "sync"]) };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        var me = table.GetTable("me")!;
        Assert.Equal("123456789012345678", me.GetString("id"));
        Assert.Equal("admin", me.GetString("role"));
        Assert.Equal(["steward", "sync"], me.GetTable("features")!.Items.Select(i => i.Text));
    }

    [Fact]
    public void Render_OmitsTheRoleKey_WhenMeCarriesNoRole()
    {
        var payload = SamplePayload() with { Me = new SyncMe("123456789012345678", null, ["sync"]) };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"];

        Assert.Null(table.GetTable("me")!.GetString("role"));
    }

    private static DirectoryProfessions SampleProfessions() => new(
        "Player-4395-0A1B2C3D",
        "Hoobi",
        1,
        [new DirectorySkill("Alchemy", 285, 300, false)],
        new Dictionary<string, IReadOnlyList<int>> { ["Alchemy"] = [11460, 11461] });

    [Fact]
    public void Render_OmitsTheDirectoryKey_WhenThePayloadCarriesNone()
    {
        var rendered = StewardSyncFile.Render(SamplePayload());

        Assert.DoesNotContain("directory", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ProducesOnlyThePeopleAndCharactersSections_WhenOnlyRosterWasPulled()
    {
        var directory = new SyncDirectory(
            [new DirectoryPerson("111", "Hoobi", "Player-4395-0A1B2C3D")],
            [new DirectoryCharacter("Player-4395-0A1B2C3D", "Hoobi", 60, 1, "111")],
            null);
        var payload = SamplePayload() with { Directory = directory };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"].GetTable("directory")!;

        var person = Assert.Single(table.GetTable("people")!.Items);
        Assert.Equal("111", person.GetString("id"));
        Assert.Equal("Hoobi", person.GetString("name"));
        Assert.Equal("Player-4395-0A1B2C3D", person.GetString("mainGuid"));

        var character = Assert.Single(table.GetTable("characters")!.Items);
        Assert.Equal("Player-4395-0A1B2C3D", character.GetString("guid"));
        Assert.Equal(60d, character.GetNumber("level"));
        Assert.Equal(1d, character.GetNumber("classId"));
        Assert.Equal("111", character.GetString("linkedUserId"));

        Assert.Null(table.GetTable("professions"));
        Assert.Null(table.GetTable("links"));

        var links = LuaSavedVariables.Parse(asAssignment)["X"].GetTable("links")!;
        Assert.Equal("111", links.GetString("Player-4395-0A1B2C3D"));
    }

    [Fact]
    public void Render_OmitsAnUnlinkedCharacterFromLinks_ButStillWritesTheTable()
    {
        var directory = new SyncDirectory(
            [new DirectoryPerson("111", "Hoobi", "Player-4395-0A1B2C3D")],
            [
                new DirectoryCharacter("Player-4395-0A1B2C3D", "Hoobi", 60, 1, "111"),
                new DirectoryCharacter("Player-9999-0A1B2C3D", "Grug", 60, 2, null),
            ],
            null);
        var payload = SamplePayload() with { Directory = directory };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var parsed = LuaSavedVariables.Parse(asAssignment)["X"];
        Assert.Null(parsed.GetTable("directory")!.GetTable("links"));

        var links = parsed.GetTable("links")!;
        Assert.Equal("111", links.GetString("Player-4395-0A1B2C3D"));
        Assert.Null(links.GetString("Player-9999-0A1B2C3D"));
    }

    [Fact]
    public void Render_OmitsTheLinksKey_WhenNoRosterWasPulled()
    {
        var directory = new SyncDirectory(null, null, [SampleProfessions()]);
        var payload = SamplePayload() with { Directory = directory };
        var rendered = StewardSyncFile.Render(payload);

        Assert.DoesNotContain("links", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_Changes_WhenACharacterLinkChanges()
    {
        var linked = new SyncDirectory(
            [new DirectoryPerson("111", "Hoobi", "Player-4395-0A1B2C3D")],
            [new DirectoryCharacter("Player-4395-0A1B2C3D", "Hoobi", 60, 1, "111")],
            null);
        var unlinked = linked with { Characters = [linked.Characters![0] with { LinkedUserId = null }] };

        var linkedFingerprint = StewardSyncFile.Fingerprint(SamplePayload() with { Directory = linked });
        var unlinkedFingerprint = StewardSyncFile.Fingerprint(SamplePayload() with { Directory = unlinked });

        Assert.NotEqual(linkedFingerprint, unlinkedFingerprint);
    }

    [Fact]
    public void Render_ProducesOnlyTheProfessionsSection_WhenOnlyProfessionsWasPulled()
    {
        var directory = new SyncDirectory(null, null, [SampleProfessions()]);
        var payload = SamplePayload() with { Directory = directory };
        var rendered = StewardSyncFile.Render(payload);
        var asAssignment = rendered.Replace("Steward.LoadSync(", "X = ", StringComparison.Ordinal);
        asAssignment = asAssignment[..asAssignment.LastIndexOf(')')];

        var table = LuaSavedVariables.Parse(asAssignment)["X"].GetTable("directory")!;

        Assert.Null(table.GetTable("people"));
        Assert.Null(table.GetTable("characters"));

        var profession = Assert.Single(table.GetTable("professions")!.Items);
        Assert.Equal("Player-4395-0A1B2C3D", profession.GetString("guid"));
        Assert.Equal("Hoobi", profession.GetString("name"));
        Assert.Equal(1d, profession.GetNumber("classId"));
        var skill = Assert.Single(profession.GetTable("skills")!.Items);
        Assert.Equal("Alchemy", skill.GetString("name"));
        Assert.Equal(300d, skill.GetNumber("maxRank"));

        var recipeIds = profession.GetTable("recipes")!.GetTable("Alchemy")!.Items.Select(i => i.Number);
        Assert.Equal([11460d, 11461d], recipeIds);
    }

    [Fact]
    public void Write_CreatesTheSyncFile_WhenTheAddonIsInstalled()
    {
        var addOnsPath = InstallAddon();

        StewardSyncFile.Write(addOnsPath, SamplePayload());

        var target = Path.Combine(addOnsPath, "Steward", "StewardSync.lua");
        Assert.True(File.Exists(target));
        Assert.StartsWith("Steward.LoadSync(", File.ReadAllText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Throws_WhenTheTocIsMissing()
    {
        var addOnsPath = InstallAddon(withToc: false);

        Assert.Throws<InvalidOperationException>(() => StewardSyncFile.Write(addOnsPath, SamplePayload()));

        Assert.False(File.Exists(Path.Combine(addOnsPath, "Steward", "StewardSync.lua")));
    }

    [Fact]
    public void Write_Throws_ForAnAddOnsPathContainingAWtfSegment()
    {
        var addOnsPath = Path.Combine(_root, "WTF", "AddOns");
        var addonPath = Path.Combine(addOnsPath, "Steward");
        Directory.CreateDirectory(addonPath);
        File.WriteAllText(Path.Combine(addonPath, "Steward.toc"), "## Interface: 11507\n");

        Assert.Throws<InvalidOperationException>(() => StewardSyncFile.Write(addOnsPath, SamplePayload()));

        Assert.False(File.Exists(Path.Combine(addonPath, "StewardSync.lua")));
    }

    [Fact]
    public void Write_WritesTheAvatarAndNamesIt_WhenThePayloadCarriesOne()
    {
        var addOnsPath = InstallAddon();
        var payload = SamplePayload() with { Avatar = SampleAvatar() };

        StewardSyncFile.Write(addOnsPath, payload);

        var avatar = Path.Combine(addOnsPath, "Steward", "Avatar.tga");
        Assert.True(File.Exists(avatar));
        Assert.Equal(18 + (64 * 64 * 4), new FileInfo(avatar).Length);
        Assert.Contains(
            @"[""avatar""] = ""Interface\\AddOns\\Steward\\Avatar.tga""",
            File.ReadAllText(Path.Combine(addOnsPath, "Steward", "StewardSync.lua")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_OmitsTheAvatarKey_WhenThePayloadCarriesNoAvatar()
    {
        var addOnsPath = InstallAddon();

        StewardSyncFile.Write(addOnsPath, SamplePayload());

        Assert.False(File.Exists(Path.Combine(addOnsPath, "Steward", "Avatar.tga")));
        Assert.DoesNotContain(
            "avatar",
            File.ReadAllText(Path.Combine(addOnsPath, "Steward", "StewardSync.lua")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_OmitsTheAvatarKey_WhenTheImageCannotBeEncoded()
    {
        var addOnsPath = InstallAddon();
        var payload = SamplePayload() with
        {
            Avatar = new AvatarImage("https://cdn.discordapp.com/avatars/1/hash.png?size=64", 48, 48, new byte[48 * 48 * 4]),
        };

        StewardSyncFile.Write(addOnsPath, payload);

        Assert.False(File.Exists(Path.Combine(addOnsPath, "Steward", "Avatar.tga")));
        Assert.DoesNotContain(
            "avatar",
            File.ReadAllText(Path.Combine(addOnsPath, "Steward", "StewardSync.lua")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_LeavesNoTempFileBehind()
    {
        var addOnsPath = InstallAddon();

        StewardSyncFile.Write(addOnsPath, SamplePayload());

        Assert.Empty(Directory.GetFiles(Path.Combine(addOnsPath, "Steward"), "*.tmp"));
    }

    [Fact]
    public void Write_ReplacesAnExistingFile()
    {
        var addOnsPath = InstallAddon();
        var target = Path.Combine(addOnsPath, "Steward", "StewardSync.lua");
        File.WriteAllText(target, "Steward.LoadSync({[\"stale\"] = true})");

        StewardSyncFile.Write(addOnsPath, SamplePayload());

        Assert.DoesNotContain("stale", File.ReadAllText(target), StringComparison.Ordinal);
    }
}
