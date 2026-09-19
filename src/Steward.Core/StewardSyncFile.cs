namespace Steward.Core;

public static class StewardSyncFile
{
    public const string FileName = "StewardSync.lua";

    public static string PathFor(string addOnsPath) =>
        Path.Combine(addOnsPath, StewardSavedVariables.AddonName, FileName);

    public static string Render(SyncPayload payload) =>
        "Steward.LoadSync(" + LuaWriter.Serialize(ToLua(payload)) + ")" + Environment.NewLine;

    public static void Write(string addOnsPath, SyncPayload payload)
    {
        var tocPath = Path.Combine(addOnsPath, StewardSavedVariables.AddonName, $"{StewardSavedVariables.AddonName}.toc");
        if (!File.Exists(tocPath))
        {
            throw new InvalidOperationException(
                $"{tocPath} does not exist; the Steward addon must be installed before writing {FileName}");
        }

        var addonRoot = Path.GetFullPath(
            Path.Combine(addOnsPath, StewardSavedVariables.AddonName) + Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(PathFor(addOnsPath));
        if (!target.StartsWith(addonRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(target), FileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write outside the Steward addon folder: {target}");
        }

        if (target.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("WTF", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write a path containing a WTF segment: {target}");
        }

        var tempPath = target + ".tmp";
        File.WriteAllText(tempPath, Render(payload));
        File.Move(tempPath, target, overwrite: true);
    }

    private static LuaValue ToLua(SyncPayload payload)
    {
        var entries = new List<LuaEntry>
        {
            new(LuaValue.FromString("writtenAt"), LuaValue.FromNumber(payload.WrittenAt.ToUnixTimeSeconds())),
        };

        if (payload.ExportedAt is { } exportedAt)
        {
            entries.Add(new(LuaValue.FromString("exportedAt"), LuaValue.FromNumber(exportedAt.ToUnixTimeSeconds())));
        }

        entries.Add(new(LuaValue.FromString("roster"), LuaValue.Array(payload.Roster.Select(RosterToLua))));
        entries.Add(new(LuaValue.FromString("loot"), LuaValue.Array(payload.Loot.Select(LootToLua))));
        entries.Add(new(LuaValue.FromString("attendance"), LuaValue.Array(payload.Attendance.Select(AttendanceToLua))));

        return LuaValue.FromTable(entries);
    }

    private static LuaValue RosterToLua(RosterMember member) => LuaValue.FromTable(
        new LuaEntry(LuaValue.FromString("name"), LuaValue.FromString(member.Name)),
        new LuaEntry(LuaValue.FromString("realm"), LuaValue.FromString(member.Realm)),
        new LuaEntry(LuaValue.FromString("class"), LuaValue.FromString(member.Class)),
        new LuaEntry(LuaValue.FromString("level"), LuaValue.FromNumber(member.Level)),
        new LuaEntry(LuaValue.FromString("rank"), LuaValue.FromString(member.Rank)),
        new LuaEntry(LuaValue.FromString("rankIndex"), LuaValue.FromNumber(member.RankIndex)),
        new LuaEntry(LuaValue.FromString("note"), LuaValue.FromString(member.Note)),
        new LuaEntry(LuaValue.FromString("officerNote"), LuaValue.FromString(member.OfficerNote)),
        new LuaEntry(LuaValue.FromString("lastOnline"), OrNilTimestamp(member.LastOnline)));

    private static LuaValue LootToLua(LootEvent loot) => LuaValue.FromTable(
        new LuaEntry(LuaValue.FromString("id"), LuaValue.FromString(loot.Id)),
        new LuaEntry(LuaValue.FromString("at"), LuaValue.FromNumber(loot.At.ToUnixTimeSeconds())),
        new LuaEntry(LuaValue.FromString("player"), LuaValue.FromString(loot.Player)),
        new LuaEntry(LuaValue.FromString("itemId"), LuaValue.FromNumber(loot.ItemId)),
        new LuaEntry(LuaValue.FromString("item"), LuaValue.FromString(loot.Item)),
        new LuaEntry(LuaValue.FromString("quality"), LuaValue.FromNumber(loot.Quality)),
        new LuaEntry(LuaValue.FromString("source"), OrNil(loot.Source)),
        new LuaEntry(LuaValue.FromString("instance"), OrNil(loot.Instance)));

    private static LuaValue AttendanceToLua(AttendanceRecord attendance) => LuaValue.FromTable(
        new LuaEntry(LuaValue.FromString("id"), LuaValue.FromString(attendance.Id)),
        new LuaEntry(LuaValue.FromString("at"), LuaValue.FromNumber(attendance.At.ToUnixTimeSeconds())),
        new LuaEntry(LuaValue.FromString("instance"), LuaValue.FromString(attendance.Instance)),
        new LuaEntry(LuaValue.FromString("present"), LuaValue.Array(attendance.Present.Select(LuaValue.FromString))));

    private static LuaValue OrNil(string? value) => value is null ? LuaValue.Nil : LuaValue.FromString(value);

    private static LuaValue OrNilTimestamp(DateTimeOffset? value) =>
        value is null ? LuaValue.Nil : LuaValue.FromNumber(value.Value.ToUnixTimeSeconds());
}
