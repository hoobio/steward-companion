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

public sealed record RosterStatusDef(
    [property: JsonPropertyName("name")] string Name);

public sealed record OriginDef(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("color")] string Color);

public sealed record GuildRosterResponse(
    [property: JsonPropertyName("members")] IReadOnlyList<GuildRosterMember> Members,
    [property: JsonPropertyName("statuses")] IReadOnlyList<RosterStatusDef>? Statuses,
    [property: JsonPropertyName("origins")] IReadOnlyList<OriginDef>? Origins);

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
    int Skipped,
    IReadOnlyList<CharacterObservation> Characters,
    string? CharactersFingerprint,
    IReadOnlyDictionary<string, CharacterProfessions> Professions);

public sealed record CharacterObservation(
    string CharacterGuid,
    string Name,
    string Realm,
    string Guild,
    int Level,
    int ClassId,
    int RaceId,
    int RankIndex,
    DateTimeOffset? LastOnline,
    string? LinkedUserId,
    bool LinkKnown,
    DateTimeOffset? ObservedAt);

public sealed record CharacterSyncEntry(
    [property: JsonPropertyName("guid")] string CharacterGuid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("realm")] string Realm,
    [property: JsonPropertyName("guild")] string Guild,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("classId")] int ClassId,
    [property: JsonPropertyName("raceId")] int RaceId,
    [property: JsonPropertyName("rankIndex")] int RankIndex,
    [property: JsonPropertyName("lastOnline")] long? LastOnline,
    [property: JsonPropertyName("linkedUserId")] string? LinkedUserId,
    [property: JsonPropertyName("linkKnown")] bool LinkKnown,
    [property: JsonPropertyName("observedAt")] long? ObservedAt,
    [property: JsonPropertyName("professions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterProfessions? Professions = null);

public sealed record CharacterProfessions(
    [property: JsonPropertyName("observedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ObservedAt,
    [property: JsonPropertyName("skills"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProfessionSkill>? Skills,
    [property: JsonPropertyName("recipes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, ProfessionRecipes>? Recipes);

public sealed record ProfessionSkill(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rank"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Rank,
    [property: JsonPropertyName("maxRank"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxRank,
    [property: JsonPropertyName("secondary"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Secondary);

public sealed record ProfessionRecipes(
    [property: JsonPropertyName("scannedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ScannedAt,
    [property: JsonPropertyName("list"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProfessionRecipe>? List);

public sealed record ProfessionRecipe(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("recipeId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RecipeId,
    [property: JsonPropertyName("header"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Header,
    [property: JsonPropertyName("difficulty"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Difficulty,
    [property: JsonPropertyName("itemId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ItemId,
    [property: JsonPropertyName("tools"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tools,
    [property: JsonPropertyName("reagents"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProfessionReagent>? Reagents);

public sealed record ProfessionReagent(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("itemId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ItemId,
    [property: JsonPropertyName("count"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Count);

public sealed record CharacterSyncRequest(
    [property: JsonPropertyName("batchId")] string BatchId,
    [property: JsonPropertyName("appVersion")] string AppVersion,
    [property: JsonPropertyName("characters")] IReadOnlyList<CharacterSyncEntry> Characters);

public sealed record CharacterSyncRejection(
    [property: JsonPropertyName("guid")] string CharacterGuid,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record CharacterSyncResponse(
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("rejected")] IReadOnlyList<CharacterSyncRejection> Rejected);

public sealed record CharacterPushRecord(
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset PushedAt,
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("error")] string? Error = null);

public sealed record CharacterSyncBatch(
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("batch_id")] string BatchId);

public static class CharacterSyncMapping
{
    public static CharacterSyncEntry ToEntry(CharacterObservation observation, IReadOnlyDictionary<string, CharacterProfessions> professions) => new(
        observation.CharacterGuid,
        observation.Name,
        observation.Realm,
        observation.Guild,
        observation.Level,
        observation.ClassId,
        observation.RaceId,
        observation.RankIndex,
        observation.LastOnline?.ToUnixTimeSeconds(),
        observation.LinkedUserId,
        observation.LinkKnown,
        observation.ObservedAt?.ToUnixTimeSeconds(),
        professions.GetValueOrDefault(observation.CharacterGuid));
}

public static class CharacterPushGate
{
    public static bool ShouldPush(string? fingerprint, IReadOnlyDictionary<string, CharacterPushRecord> lastPushes, string key) =>
        fingerprint is not null
        && (!lastPushes.TryGetValue(key, out var last) || !string.Equals(last.Fingerprint, fingerprint, StringComparison.Ordinal));

    // A retry of the same fingerprint reuses its batchId so the server's idempotent replay applies; a changed fingerprint starts a new one.
    public static string ResolveBatchId(CharacterSyncBatch? pending, string fingerprint, string newBatchId) =>
        pending is not null && string.Equals(pending.Fingerprint, fingerprint, StringComparison.Ordinal)
            ? pending.BatchId
            : newBatchId;
}

public sealed record WowClientProcess(int ProcessId, DateTimeOffset StartTime);

public sealed record AvatarImage(string SourceUrl, int Width, int Height, byte[] Bgra);

public sealed record SyncPayload(
    DateTimeOffset WrittenAt,
    DateTimeOffset? ExportedAt,
    IReadOnlyList<RosterMember> Roster,
    IReadOnlyList<LootEvent> Loot,
    IReadOnlyList<AttendanceRecord> Attendance,
    IReadOnlyList<GuildRosterMember> Members,
    IReadOnlyList<DiscordMember> Discord,
    IReadOnlyList<string> Statuses)
{
    public AvatarImage? Avatar { get; init; }

    public IReadOnlyList<OriginDef> Origins { get; init; } = [];
}

public sealed record SyncServerState(
    IReadOnlyDictionary<string, int> RecordCounts,
    string Cursor,
    DateTimeOffset? LastSyncedAt);

public sealed record SyncPushResult(bool Accepted, int Taken, string? Error);
