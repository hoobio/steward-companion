using System.Security.Cryptography;
using System.Text;

namespace Steward.Core;

public static class StewardSyncFile
{
    public const string FileName = "StewardSync.lua";

    public const string AvatarFileName = "Avatar.tga";

    private static readonly string AvatarTexturePath =
        $@"Interface\AddOns\{StewardSavedVariables.AddonName}\{AvatarFileName}";

    public static string PathFor(string addOnsPath) =>
        Path.Combine(addOnsPath, StewardSavedVariables.AddonName, FileName);

    public static string AvatarPathFor(string addOnsPath) =>
        Path.Combine(addOnsPath, StewardSavedVariables.AddonName, AvatarFileName);

    public static string Render(SyncPayload payload, bool withAvatar = false) =>
        "Steward.LoadSync(" + LuaWriter.Serialize(ToLua(payload, withAvatar)) + ")" + Environment.NewLine;

    public static string Fingerprint(SyncPayload payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Render(payload with { WrittenAt = DateTimeOffset.UnixEpoch }) + payload.Avatar?.SourceUrl)));

    public static void Write(string addOnsPath, SyncPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var tocPath = Path.Combine(addOnsPath, StewardSavedVariables.AddonName, $"{StewardSavedVariables.AddonName}.toc");
        if (!File.Exists(tocPath))
        {
            throw new InvalidOperationException(
                $"{tocPath} does not exist; the Steward addon must be installed before writing {FileName}");
        }

        var wroteAvatar = TryWriteAvatar(addOnsPath, payload.Avatar);
        WriteGuarded(addOnsPath, FileName, Encoding.UTF8.GetBytes(Render(payload, wroteAvatar)));
    }

    private static bool TryWriteAvatar(string addOnsPath, AvatarImage? avatar)
    {
        if (avatar is null)
        {
            return false;
        }

        try
        {
            WriteGuarded(addOnsPath, AvatarFileName, AvatarTga.Encode(avatar));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteGuarded(string addOnsPath, string fileName, byte[] contents)
    {
        var addonRoot = Path.GetFullPath(
            Path.Combine(addOnsPath, StewardSavedVariables.AddonName) + Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(addOnsPath, StewardSavedVariables.AddonName, fileName));
        if (!target.StartsWith(addonRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(target), fileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write outside the Steward addon folder: {target}");
        }

        if (target.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("WTF", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write a path containing a WTF segment: {target}");
        }

        var tempPath = target + ".tmp";
        File.WriteAllBytes(tempPath, contents);
        File.Move(tempPath, target, overwrite: true);
    }

    private static LuaValue ToLua(SyncPayload payload, bool withAvatar)
    {
        var entries = new List<LuaEntry>
        {
            new(LuaValue.FromString("writtenAt"), LuaValue.FromNumber(payload.WrittenAt.ToUnixTimeSeconds())),
        };

        if (payload.ExportedAt is { } exportedAt)
        {
            entries.Add(new(LuaValue.FromString("exportedAt"), LuaValue.FromNumber(exportedAt.ToUnixTimeSeconds())));
        }

        if (withAvatar)
        {
            // GetTexture() echoes back any path it was given and a texture that failed to load draws solid green, so the key's presence is the addon's only signal the file is really there.
            entries.Add(new(LuaValue.FromString("avatar"), LuaValue.FromString(AvatarTexturePath)));
        }

        entries.Add(new(LuaValue.FromString("roster"), LuaValue.Array(payload.Roster.Select(RosterToLua))));
        entries.Add(new(LuaValue.FromString("loot"), LuaValue.Array(payload.Loot.Select(LootToLua))));
        entries.Add(new(LuaValue.FromString("attendance"), LuaValue.Array(payload.Attendance.Select(AttendanceToLua))));
        entries.Add(new(LuaValue.FromString("members"), LuaValue.Array(payload.Members.Select(MemberToLua))));
        entries.Add(new(LuaValue.FromString("discord"), LuaValue.Array(payload.Discord.Select(DiscordToLua))));

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

    private static LuaValue MemberToLua(GuildRosterMember member) => LuaValue.FromTable(
        new LuaEntry(LuaValue.FromString("userId"), LuaValue.FromString(member.UserId)),
        new LuaEntry(LuaValue.FromString("name"), LuaValue.FromString(member.Name)),
        new LuaEntry(LuaValue.FromString("displayName"), OrNil(member.DisplayName)),
        new LuaEntry(LuaValue.FromString("discordTag"), OrNil(member.DiscordTag)),
        new LuaEntry(LuaValue.FromString("status"), OrNil(member.Status)),
        new LuaEntry(LuaValue.FromString("origin"), LuaValue.Array(member.Origin.Select(LuaValue.FromString))),
        new LuaEntry(LuaValue.FromString("flags"), LuaValue.Array(member.Flags.Select(LuaValue.FromString))),
        new LuaEntry(LuaValue.FromString("rating"), member.Rating is { } rating ? LuaValue.FromNumber(rating) : LuaValue.Nil),
        new LuaEntry(LuaValue.FromString("notes"), OrNil(member.Notes)),
        new LuaEntry(LuaValue.FromString("notesWarning"), LuaValue.FromBoolean(member.NotesWarning)),
        new LuaEntry(LuaValue.FromString("signups"), LuaValue.FromNumber(member.Signups)),
        new LuaEntry(LuaValue.FromString("lastSignupAt"), LuaValue.FromNumber(member.LastSignupAt)),
        new LuaEntry(LuaValue.FromString("primary"), BuildToLua(member.Primary)),
        new LuaEntry(LuaValue.FromString("secondary"), BuildToLua(member.Secondary)));

    private static LuaValue BuildToLua(GuildBuild? build) => build is null
        ? LuaValue.Nil
        : LuaValue.FromTable(
            new LuaEntry(LuaValue.FromString("class"), LuaValue.FromString(build.Class)),
            new LuaEntry(LuaValue.FromString("spec"), LuaValue.FromString(build.Spec)),
            new LuaEntry(LuaValue.FromString("role"), LuaValue.FromString(build.Role)));

    private static LuaValue DiscordToLua(DiscordMember member) => LuaValue.FromTable(
        new LuaEntry(LuaValue.FromString("id"), LuaValue.FromString(member.Id)),
        new LuaEntry(LuaValue.FromString("name"), LuaValue.FromString(member.Name)),
        new LuaEntry(LuaValue.FromString("nick"), OrNil(member.Nick)));

    private static LuaValue OrNil(string? value) => value is null ? LuaValue.Nil : LuaValue.FromString(value);

    private static LuaValue OrNilTimestamp(DateTimeOffset? value) =>
        value is null ? LuaValue.Nil : LuaValue.FromNumber(value.Value.ToUnixTimeSeconds());
}
