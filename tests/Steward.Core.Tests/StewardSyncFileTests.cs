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

        var discord = Assert.Single(table.GetTable("discord")!.Items);
        Assert.Equal("222", discord.GetString("id"));
        Assert.Equal("Grug", discord.GetString("name"));
        Assert.Null(discord.GetString("nick"));
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
                        [new ProfessionReagent("Golden Sansam", 13464, 2)]),
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

        var reagent = Assert.Single(recipe.GetTable("reagents")!.Items);
        Assert.Equal(13464d, reagent.GetNumber("itemId"));
        Assert.Equal("Golden Sansam", reagent.GetString("name"));
        Assert.Equal(2d, reagent.GetNumber("count"));
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
