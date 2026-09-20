using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed record GuildBuild(
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("spec")] string Spec,
    [property: JsonPropertyName("role")] string Role);

public sealed record GuildRosterMember(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("discord_tag")] string? DiscordTag,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("origin")] IReadOnlyList<string> Origin,
    [property: JsonPropertyName("flags")] IReadOnlyList<string> Flags,
    [property: JsonPropertyName("rating")] int? Rating,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("notes_warning")] bool NotesWarning,
    [property: JsonPropertyName("signups")] int Signups,
    [property: JsonPropertyName("last_signup_at")] long LastSignupAt,
    [property: JsonPropertyName("primary")] GuildBuild? Primary,
    [property: JsonPropertyName("secondary")] GuildBuild? Secondary);

public sealed record DiscordMember(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("nick")] string? Nick);

public sealed record GuildRosterResponse(
    [property: JsonPropertyName("members")] IReadOnlyList<GuildRosterMember> Members);

public sealed record DiscordMembersResponse(
    [property: JsonPropertyName("members")] IReadOnlyList<DiscordMember> Members);

public sealed record RosterMember(
    string Name,
    string Realm,
    string Class,
    int Level,
    string Rank,
    int RankIndex,
    string Note,
    string OfficerNote,
    DateTimeOffset? LastOnline);

public sealed record LootEvent(
    string Id,
    DateTimeOffset At,
    string Player,
    int ItemId,
    string Item,
    int Quality,
    string? Source,
    string? Instance);

public sealed record AttendanceRecord(
    string Id,
    DateTimeOffset At,
    string Instance,
    IReadOnlyList<string> Present);

public sealed record SavedVariablesFile(
    string Path,
    DateTimeOffset LastWriteTime,
    DateTimeOffset? ExportedAt,
    string? Character);

public sealed record SavedVariablesSnapshot(
    IReadOnlyList<SavedVariablesFile> Files,
    DateTimeOffset? ExportedAt,
    IReadOnlyList<RosterMember> Roster,
    IReadOnlyList<LootEvent> Loot,
    IReadOnlyList<AttendanceRecord> Attendance,
    int Skipped);

public sealed record WowClientProcess(int ProcessId, DateTimeOffset StartTime);

public sealed record AvatarImage(string SourceUrl, int Width, int Height, byte[] Bgra);

public sealed record SyncPayload(
    DateTimeOffset WrittenAt,
    DateTimeOffset? ExportedAt,
    IReadOnlyList<RosterMember> Roster,
    IReadOnlyList<LootEvent> Loot,
    IReadOnlyList<AttendanceRecord> Attendance,
    IReadOnlyList<GuildRosterMember> Members,
    IReadOnlyList<DiscordMember> Discord)
{
    public AvatarImage? Avatar { get; init; }
}

public sealed record SyncServerState(
    IReadOnlyDictionary<string, int> RecordCounts,
    string Cursor,
    DateTimeOffset? LastSyncedAt);

public sealed record SyncPushResult(bool Accepted, int Taken, string? Error);
