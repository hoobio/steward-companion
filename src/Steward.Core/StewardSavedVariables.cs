using System.Globalization;

namespace Steward.Core;

public static class StewardSavedVariables
{
    public const string AddonName = "Steward";

    private const string AccountGlobal = "StewardDB";
    private const string CharacterGlobal = "StewardCharDB";

    public static IReadOnlyList<string> FindFiles(string flavourPath)
    {
        var accountRoot = Path.Combine(flavourPath, "WTF", "Account");
        if (!Directory.Exists(accountRoot))
        {
            return [];
        }

        var files = new List<string>();
        foreach (var accountPath in Directory.EnumerateDirectories(accountRoot))
        {
            AddIfExists(files, accountPath);
            foreach (var realmPath in Directory.EnumerateDirectories(accountPath))
            {
                if (string.Equals(Path.GetFileName(realmPath), "SavedVariables", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var characterPath in Directory.EnumerateDirectories(realmPath))
                {
                    AddIfExists(files, characterPath);
                }
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static SavedVariablesSnapshot? Read(string flavourPath)
    {
        var paths = FindFiles(flavourPath);
        return paths.Count == 0
            ? null
            : Read(paths.Select(path => (path, (DateTimeOffset)File.GetLastWriteTimeUtc(path), File.ReadAllText(path))));
    }

    internal static SavedVariablesSnapshot Read(IEnumerable<(string Path, DateTimeOffset LastWriteTime, string Text)> files)
    {
        var descriptors = new List<SavedVariablesFile>();
        var roster = new List<RosterMember>();
        var loot = new List<(string Id, DateTimeOffset Rank, LootEvent Item)>();
        var attendance = new List<(string Id, DateTimeOffset Rank, AttendanceRecord Item)>();
        var characters = new List<(string Id, DateTimeOffset Rank, CharacterObservation Item)>();
        var professions = new List<(string Id, DateTimeOffset Rank, CharacterProfessions Item)>();
        var catalogue = new List<(string Id, DateTimeOffset Rank, ProfessionCatalogue Item)>();
        var gear = new List<(string Id, DateTimeOffset Rank, CharacterGear Item)>();
        var scans = new List<FileScan>();
        GuildRanks? guildRanks = null;
        var hasAccount = false;
        var skipped = 0;
        var outdatedProfessions = 0;

        foreach (var (path, lastWriteTime, text) in files)
        {
            var globals = LuaSavedVariables.Parse(text);
            var account = globals.GetValueOrDefault(AccountGlobal);
            var root = account ?? globals.GetValueOrDefault(CharacterGlobal);
            var exportedAt = ToTimestamp(root?.GetNumber("exportedAt"));
            var character = root?.GetTable("character")?.GetString("name");
            descriptors.Add(new SavedVariablesFile(path, lastWriteTime, exportedAt, character));

            if (root is null)
            {
                continue;
            }

            var rank = exportedAt ?? DateTimeOffset.MinValue;
            if (account is not null)
            {
                roster.AddRange(MapAll(account.GetTable("roster"), MapRoster, ref skipped));
                var fileCharacters = MapCharacters(account.GetTable("characters"), ref skipped);
                var fileProfessions = MapProfessionsByGuid(account.GetTable("professions"), ref skipped, ref outdatedProfessions);
                var fileGear = MapGearByGuid(account.GetTable("gear"), ref skipped);
                professions.AddRange(fileProfessions
                    .Select(p => (p.Guid, ToTimestamp(p.Professions.ObservedAt) ?? DateTimeOffset.MinValue, p.Professions)));
                gear.AddRange(fileGear
                    .Select(g => (g.Guid, ToTimestamp(g.Gear.ObservedAt) ?? DateTimeOffset.MinValue, g.Gear)));
                scans.Add(new FileScan(
                    fileCharacters,
                    [.. fileProfessions.Select(p => p.Guid), .. fileGear.Select(g => g.Guid)],
                    MapGuildRanks(account.GetTable("guildRanks"), ref skipped)));
                catalogue.AddRange(MapCatalogueByProfession(account.GetTable("catalogue"), ref skipped)
                    .Select(c => (c.Profession, ToTimestamp(c.Catalogue.ScannedAt) ?? DateTimeOffset.MinValue, c.Catalogue)));
                if (scans[^1].Ranks is { } ranks
                    && (guildRanks is null || ranks.ObservedAt is null || guildRanks.ObservedAt is null || ranks.ObservedAt > guildRanks.ObservedAt))
                {
                    guildRanks = ranks;
                }

                hasAccount = true;
            }

            loot.AddRange(MapAll(root.GetTable("loot"), MapLoot, ref skipped).Select(r => (r.Id, rank, r)));
            attendance.AddRange(MapAll(root.GetTable("attendance"), MapAttendance, ref skipped).Select(r => (r.Id, rank, r)));
        }

        foreach (var scan in scans)
        {
            characters.AddRange(scan.Characters
                .Where(c => scan.OwnGuids.Contains(c.CharacterGuid) || !IsStaleRoster(c, scan, scans))
                .Select(c => (c.CharacterGuid, c.ObservedAt ?? DateTimeOffset.MinValue, c)));
        }

        var dedupedCharacters = Dedupe(characters);
        var dedupedProfessions = DedupeByKey(professions);
        var dedupedCatalogue = DedupeByKey(catalogue);
        var dedupedGear = DedupeByKey(gear);
        return new SavedVariablesSnapshot(
            descriptors,
            descriptors.Select(f => f.ExportedAt).Max(),
            roster,
            Dedupe(loot),
            Dedupe(attendance),
            skipped,
            dedupedCharacters,
            hasAccount ? CharacterSyncMapping.Fingerprint(dedupedCharacters, dedupedProfessions, dedupedCatalogue, guildRanks, dedupedGear) : null,
            dedupedProfessions,
            dedupedCatalogue,
            guildRanks,
            hasAccount,
            outdatedProfessions)
        {
            Gear = dedupedGear,
        };
    }

    private static readonly TimeSpan StaleGuildScan = TimeSpan.FromHours(24);

    private sealed record FileScan(IReadOnlyList<CharacterObservation> Characters, HashSet<string> OwnGuids, GuildRanks? Ranks);

    private static bool IsStaleRoster(CharacterObservation character, FileScan scan, List<FileScan> all)
    {
        var own = character.Guild.Length == 0 ? null : ScanTime(character, scan);
        return own is not null && all.Max(other => ScanTime(character, other)) - own > StaleGuildScan;
    }

    private static DateTimeOffset? ScanTime(CharacterObservation member, FileScan scan)
    {
        var ranks = scan.Ranks is { } r && SameGuildName(member.Guild, r.Guild) && RanksRealmMatches(member, r, scan) ? r.ObservedAt : null;
        var members = scan.Characters.Where(c => SameGuild(member, c)).Max(c => c.ObservedAt);
        return ranks ?? members;
    }

    private static bool RanksRealmMatches(CharacterObservation member, GuildRanks ranks, FileScan scan)
    {
        var ids = scan.Characters.Where(c => SameGuildName(c.Guild, ranks.Guild)).Select(c => RealmId(c.CharacterGuid)).OfType<string>().ToList();
        return RealmId(member.CharacterGuid) is { } id && ids.Count > 0
            ? ids.Contains(id)
            : SameRealmText(member.Realm, member.RealmName, ranks.Realm, ranks.RealmName);
    }

    private static bool SameGuild(CharacterObservation a, CharacterObservation b) =>
        SameGuildName(a.Guild, b.Guild)
        && (RealmId(a.CharacterGuid) is { } idA && RealmId(b.CharacterGuid) is { } idB
            ? idA == idB
            : SameRealmText(a.Realm, a.RealmName, b.Realm, b.RealmName));

    private static bool SameGuildName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameRealmText(string realmA, string? realmNameA, string realmB, string? realmNameB) =>
        string.Equals(realmA, realmB, StringComparison.OrdinalIgnoreCase)
        || (realmNameA is not null && string.Equals(realmNameA, realmNameB, StringComparison.OrdinalIgnoreCase));

    private static string? RealmId(string guid)
    {
        var parts = guid.Split('-');
        return parts.Length >= 3 && parts[0] == "Player" && parts[1].Length > 0 && parts[1].All(char.IsAsciiDigit) ? parts[1] : null;
    }

    private static Dictionary<string, T> DedupeByKey<T>(List<(string Id, DateTimeOffset Rank, T Item)> records) =>
        records.GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.MaxBy(r => r.Rank).Item, StringComparer.Ordinal);

    private static void AddIfExists(List<string> files, string scopePath)
    {
        var path = Path.Combine(scopePath, "SavedVariables", AddonName + ".lua");
        if (File.Exists(path))
        {
            files.Add(path);
        }
    }

    private static List<T> MapAll<T>(LuaValue? table, Func<LuaValue, T?> map, ref int skipped)
        where T : class
    {
        var mapped = new List<T>();
        foreach (var entry in table?.Items ?? [])
        {
            var record = entry.Kind is LuaKind.Table ? map(entry) : null;
            if (record is null)
            {
                skipped++;
            }
            else
            {
                mapped.Add(record);
            }
        }

        return mapped;
    }

    private static IReadOnlyList<T> Dedupe<T>(List<(string Id, DateTimeOffset Rank, T Item)> records) =>
        [.. records.GroupBy(r => r.Id, StringComparer.Ordinal).Select(g => g.MaxBy(r => r.Rank).Item)];

    private static RosterMember? MapRoster(LuaValue value)
    {
        var name = value.GetString("name");
        var realm = value.GetString("realm");
        if (name is null || realm is null)
        {
            return null;
        }

        return new RosterMember(
            name,
            realm,
            value.GetString("class") ?? string.Empty,
            ToInt(value.GetNumber("level")),
            value.GetString("rank") ?? string.Empty,
            ToInt(value.GetNumber("rankIndex")),
            value.GetString("note") ?? string.Empty,
            value.GetString("officerNote") ?? string.Empty,
            ToTimestamp(value.GetNumber("lastOnline")));
    }

    private static LootEvent? MapLoot(LuaValue value)
    {
        var id = value.GetString("id");
        var at = ToTimestamp(value.GetNumber("at"));
        var player = value.GetString("player");
        if (id is null || at is null || player is null)
        {
            return null;
        }

        return new LootEvent(
            id,
            at.Value,
            player,
            ToInt(value.GetNumber("itemId")),
            value.GetString("item") ?? string.Empty,
            ToInt(value.GetNumber("quality")),
            value.GetString("source"),
            value.GetString("instance"));
    }

    private static AttendanceRecord? MapAttendance(LuaValue value)
    {
        var id = value.GetString("id");
        var at = ToTimestamp(value.GetNumber("at"));
        var instance = value.GetString("instance");
        if (id is null || at is null || instance is null)
        {
            return null;
        }

        var present = value.GetTable("present")?.Items
            .Where(item => item.Kind is LuaKind.Text)
            .Select(item => item.Text!)
            .ToList() ?? [];

        return new AttendanceRecord(id, at.Value, instance, present);
    }

    private static List<CharacterObservation> MapCharacters(LuaValue? table, ref int skipped)
    {
        var mapped = new List<CharacterObservation>();
        foreach (var entry in table?.Table ?? [])
        {
            var record = entry.Key is { Kind: LuaKind.Text } key && entry.Value.Kind is LuaKind.Table
                ? MapCharacter(key.Text!, entry.Value)
                : null;
            if (record is null)
            {
                skipped++;
            }
            else
            {
                mapped.Add(record);
            }
        }

        return mapped;
    }

    private static CharacterObservation? MapCharacter(string guid, LuaValue value)
    {
        var name = value.GetString("name");
        var realm = value.GetString("realm");
        if (string.IsNullOrEmpty(guid) || name is null || realm is null)
        {
            return null;
        }

        return new CharacterObservation(
            guid,
            name,
            realm,
            value.GetString("guild") ?? string.Empty,
            ToInt(value.GetNumber("level")),
            ToInt(value.GetNumber("classID")),
            ToInt(value.GetNumber("raceID")),
            ToInt(value.GetNumber("rankIndex")),
            ToTimestamp(value.GetNumber("lastOnline")),
            value.GetString("linkedUserId"),
            value.Get("linkKnown") is { Kind: LuaKind.Boolean, Boolean: true },
            ToTimestamp(value.GetNumber("observedAt")),
            value.GetString("realmName"),
            value.GetNumber("gender") is 2d or 3d ? ToInt(value.GetNumber("gender")) : null,
            value.GetString("fp"),
            value.GetString("addonVersion"));
    }

    private static GuildRanks? MapGuildRanks(LuaValue? table, ref int skipped)
    {
        if (table is null)
        {
            return null;
        }

        var realm = table.GetString("realm");
        var guild = table.GetString("guild");
        if (realm is null || guild is null)
        {
            skipped++;
            return null;
        }

        var ranks = new Dictionary<int, string>();
        var position = 0;
        foreach (var entry in table.GetTable("ranks")?.Table ?? [])
        {
            // the client dumps a sequential table as positional items ("Guild Master", -- [1]), with no [n] key
            var index = entry.Key is null ? ++position : entry.Key is { Kind: LuaKind.Number } key ? (int)key.Number : (int?)null;
            if (index is { } rank && entry.Value.Kind is LuaKind.Text)
            {
                ranks[rank] = entry.Value.Text!;
            }
            else
            {
                skipped++;
            }
        }

        if (ranks.Count == 0)
        {
            skipped++;
            return null;
        }

        return new GuildRanks(realm, guild, ToTimestamp(table.GetNumber("observedAt")), ranks, table.GetString("realmName"), table.GetString("fp"), table.GetString("addonVersion"));
    }

    private static List<(string Guid, CharacterProfessions Professions)> MapProfessionsByGuid(
        LuaValue? table, ref int skipped, ref int outdated)
    {
        var mapped = new List<(string, CharacterProfessions)>();
        foreach (var entry in table?.Table ?? [])
        {
            if (entry.Key is not { Kind: LuaKind.Text } key || entry.Value.Kind is not LuaKind.Table)
            {
                skipped++;
                continue;
            }

            var professions = MapProfessions(entry.Value, ref skipped);
            if (professions is null)
            {
                skipped++;
                outdated++;
                continue;
            }

            mapped.Add((key.Text!, professions));
        }

        return mapped;
    }

    private static CharacterProfessions? MapProfessions(LuaValue value, ref int skipped)
    {
        if (ToNullableInt(value.GetNumber("schema")) != ProfessionsSchema.Current)
        {
            return null;
        }

        IReadOnlyDictionary<string, IReadOnlyList<int>>? recipes = null;
        if (value.GetTable("recipes") is { } recipesTable)
        {
            recipes = MapRecipesByProfession(recipesTable);
            if (recipes is null)
            {
                return null;
            }
        }

        return new CharacterProfessions(
            ToNullableLong(value.GetNumber("observedAt")),
            MapProfessionSkills(value.GetTable("skills"), ref skipped),
            recipes,
            value.GetString("fp"),
            AddonVersion: value.GetString("addonVersion"));
    }

    private static List<(string Guid, CharacterGear Gear)> MapGearByGuid(LuaValue? table, ref int skipped)
    {
        var mapped = new List<(string, CharacterGear)>();
        foreach (var entry in table?.Table ?? [])
        {
            var gear = entry.Value.Kind is LuaKind.Table ? MapGear(entry.Value) : null;
            if (entry.Key is not { Kind: LuaKind.Text } key || gear is null)
            {
                skipped++;
                continue;
            }

            mapped.Add((key.Text!, gear));
        }

        return mapped;
    }

    private static CharacterGear? MapGear(LuaValue value)
    {
        if (ToNullableInt(value.GetNumber("schema")) != GearSchema.Current)
        {
            return null;
        }

        var equipped = MapEquippedGear(value.GetTable("equipped"));
        var bags = MapGearEntries(value.GetTable("bags"));
        GearBank? bank = null;
        if (value.GetTable("bank") is { } bankTable)
        {
            var items = MapGearEntries(bankTable.GetTable("items"));
            if (items is null)
            {
                return null;
            }

            bank = new GearBank(ToNullableLong(bankTable.GetNumber("observedAt")), items);
        }

        return equipped is null || bags is null
            ? null
            : new CharacterGear(
                GearSchema.Current,
                ToNullableLong(value.GetNumber("observedAt")),
                ToInt(value.GetNumber("level")),
                equipped,
                bags,
                bank,
                value.GetString("fp"),
                value.GetString("addonVersion"));
    }

    private static SortedDictionary<string, GearEntry>? MapEquippedGear(LuaValue? table)
    {
        var mapped = new SortedDictionary<string, GearEntry>(StringComparer.Ordinal);
        var position = 0;
        foreach (var entry in table?.Table ?? [])
        {
            var slot = entry.Key is null ? ++position : entry.Key is { Kind: LuaKind.Number } key ? (int)key.Number : (int?)null;
            if (entry.Value.Kind is LuaKind.Nil)
            {
                continue;
            }

            var gear = entry.Value.Kind is LuaKind.Table ? MapGearEntry(entry.Value) : null;
            if (slot is null || gear is null)
            {
                return null;
            }

            mapped[slot.Value.ToString(CultureInfo.InvariantCulture)] = gear;
        }

        return mapped;
    }

    private static List<GearEntry>? MapGearEntries(LuaValue? table)
    {
        var mapped = new List<GearEntry>();
        if (table is null)
        {
            return mapped;
        }

        if (table.Table.Count != table.Items.Count)
        {
            return null;
        }

        foreach (var item in table.Items)
        {
            var entry = item.Kind is LuaKind.Table ? MapGearEntry(item) : null;
            if (entry is null)
            {
                return null;
            }

            mapped.Add(entry);
        }

        return mapped;
    }

    private static GearEntry? MapGearEntry(LuaValue value)
    {
        var link = value.GetString("link");
        var itemId = ToNullableInt(value.GetNumber("itemID"));
        var quality = ToNullableInt(value.GetNumber("quality"));
        return link is null || itemId is null || quality is null
            ? null
            : new GearEntry(
                link,
                itemId.Value,
                quality.Value,
                ToNullableInt(value.GetNumber("enchantID")),
                ToNullableInt(value.GetNumber("suffixID")),
                ToNullableInt(value.GetNumber("count")),
                value.GetString("equipLoc"),
                ToNullableInt(value.GetNumber("ilvl")));
    }

    private static List<ProfessionSkill>? MapProfessionSkills(LuaValue? table, ref int skipped)
    {
        if (table is null)
        {
            return null;
        }

        var mapped = new List<ProfessionSkill>();
        foreach (var entry in table.Items)
        {
            var skill = entry.Kind is LuaKind.Table ? MapProfessionSkill(entry) : null;
            if (skill is null)
            {
                skipped++;
            }
            else
            {
                mapped.Add(skill);
            }
        }

        return mapped;
    }

    private static ProfessionSkill? MapProfessionSkill(LuaValue value)
    {
        var name = value.GetString("name");
        if (name is null)
        {
            return null;
        }

        return new ProfessionSkill(
            name,
            ToNullableInt(value.GetNumber("rank")),
            ToNullableInt(value.GetNumber("maxRank")),
            value.Get("secondary") is { Kind: LuaKind.Boolean } secondary ? secondary.Boolean : null);
    }

    // schema 2: recipes[profession] is a bare array of positive recipe ids; anything else (including the
    // schema-1 { scannedAt, list = {...} } shape) fails the whole professions entry rather than being read lossily.
    private static Dictionary<string, IReadOnlyList<int>>? MapRecipesByProfession(LuaValue table)
    {
        var mapped = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (var entry in table.Table)
        {
            if (entry.Key is not { Kind: LuaKind.Text } key || entry.Value.Kind is not LuaKind.Table)
            {
                return null;
            }

            var ids = MapRecipeIds(entry.Value);
            if (ids is null)
            {
                return null;
            }

            mapped[key.Text!] = ids;
        }

        return mapped;
    }

    private static List<int>? MapRecipeIds(LuaValue table)
    {
        if (table.Table.Count != table.Items.Count)
        {
            // A keyed entry here (e.g. schema 1's ["scannedAt"]/["list"]) means this isn't a bare id array.
            return null;
        }

        var ids = new List<int>();
        foreach (var entry in table.Items)
        {
            if (entry.Kind is not LuaKind.Number || entry.Number is not (>= 1 and <= int.MaxValue)
                || entry.Number != Math.Floor(entry.Number))
            {
                return null;
            }

            ids.Add((int)entry.Number);
        }

        return ids;
    }

    private static List<ProfessionReagent>? MapReagents(LuaValue? table, ref int skipped)
    {
        if (table is null)
        {
            return null;
        }

        var mapped = new List<ProfessionReagent>();
        foreach (var entry in table.Items)
        {
            var reagent = entry.Kind is LuaKind.Table ? MapReagent(entry) : null;
            if (reagent is null)
            {
                skipped++;
            }
            else
            {
                mapped.Add(reagent);
            }
        }

        return mapped;
    }

    private static ProfessionReagent MapReagent(LuaValue value) =>
        new(value.GetString("name"), ToNullableInt(value.GetNumber("itemId")), ToNullableInt(value.GetNumber("count")));

    private static List<(string Profession, ProfessionCatalogue Catalogue)> MapCatalogueByProfession(LuaValue? table, ref int skipped)
    {
        var mapped = new List<(string, ProfessionCatalogue)>();
        foreach (var entry in table?.Table ?? [])
        {
            if (entry.Key is { Kind: LuaKind.Text } key && entry.Value.Kind is LuaKind.Table)
            {
                mapped.Add((key.Text!, MapCatalogue(entry.Value, ref skipped)));
            }
            else
            {
                skipped++;
            }
        }

        return mapped;
    }

    private static ProfessionCatalogue MapCatalogue(LuaValue value, ref int skipped) => new(
        ToNullableLong(value.GetNumber("scannedAt")),
        MapCatalogueRecipeList(value.GetTable("list"), ref skipped),
        value.GetString("fp"),
        value.GetString("addonVersion"));

    private static List<CatalogueRecipe>? MapCatalogueRecipeList(LuaValue? table, ref int skipped)
    {
        if (table is null)
        {
            return null;
        }

        var mapped = new List<CatalogueRecipe>();
        foreach (var entry in table.Items)
        {
            var recipe = entry.Kind is LuaKind.Table ? MapCatalogueRecipe(entry, ref skipped) : null;
            if (recipe is null)
            {
                skipped++;
            }
            else
            {
                mapped.Add(recipe);
            }
        }

        return mapped;
    }

    private static CatalogueRecipe? MapCatalogueRecipe(LuaValue value, ref int skipped)
    {
        var name = value.GetString("name");
        if (name is null)
        {
            return null;
        }

        return new CatalogueRecipe(
            name,
            ToNullableInt(value.GetNumber("recipeId")),
            value.GetString("header"),
            ToNullableInt(value.GetNumber("itemId")),
            value.GetString("tools"),
            MapReagents(value.GetTable("reagents"), ref skipped),
            ToNullableInt(value.GetNumber("order")),
            ToNullableInt(value.GetNumber("grey")),
            ToNullableInt(value.GetNumber("orangeTo")),
            ToNullableInt(value.GetNumber("yellowFrom")),
            ToNullableInt(value.GetNumber("yellowTo")),
            ToNullableInt(value.GetNumber("greenFrom")));
    }

    private static int? ToNullableInt(double? value) => value is null ? null : (int)value.Value;

    private static long? ToNullableLong(double? value) => value is null ? null : (long)value.Value;

    private static int ToInt(double? value) => value is null ? 0 : (int)value.Value;

    private static DateTimeOffset? ToTimestamp(double? value)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)value.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
