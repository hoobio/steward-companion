using System.Security.Cryptography;
using System.Text;

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
        var characters = new List<CharacterObservation>();
        var characterFingerprintSource = new StringBuilder();
        var skipped = 0;

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
                characters.AddRange(MapCharacters(account.GetTable("characters"), ref skipped));
                characterFingerprintSource.Append(text);
            }

            loot.AddRange(MapAll(root.GetTable("loot"), MapLoot, ref skipped).Select(r => (r.Id, rank, r)));
            attendance.AddRange(MapAll(root.GetTable("attendance"), MapAttendance, ref skipped).Select(r => (r.Id, rank, r)));
        }

        return new SavedVariablesSnapshot(
            descriptors,
            descriptors.Select(f => f.ExportedAt).Max(),
            roster,
            Dedupe(loot),
            Dedupe(attendance),
            skipped,
            characters,
            characterFingerprintSource.Length == 0 ? null : Fingerprint(characterFingerprintSource.ToString()));
    }

    private static string Fingerprint(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

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
            ToTimestamp(value.GetNumber("observedAt")));
    }

    private static int ToInt(double? value) => value is null ? 0 : (int)value.Value;

    private static DateTimeOffset? ToTimestamp(double? value) =>
        value is null ? null : DateTimeOffset.FromUnixTimeSeconds((long)value.Value);
}
