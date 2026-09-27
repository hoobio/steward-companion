using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed record GuildBuild(
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("spec")] string Spec,
    [property: JsonPropertyName("role")] string Role);

public sealed record GuildMain(
    [property: JsonPropertyName("guid")] string CharacterGuid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("class_id")] int ClassId);

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
    [property: JsonPropertyName("secondary")] GuildBuild? Secondary,
    [property: JsonPropertyName("main")] GuildMain? Main = null);

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
    IReadOnlyDictionary<string, CharacterProfessions> Professions,
    IReadOnlyDictionary<string, ProfessionCatalogue> Catalogue,
    GuildRanks? GuildRanks = null,
    bool HasAccountData = false)
{
    public bool HasExportedData => Characters.Count > 0 || Professions.Count > 0 || Catalogue.Count > 0 || GuildRanks is not null;

    public SyncExportState ExportState => true switch
    {
        _ when !HasAccountData => SyncExportState.NoFile,
        _ when !HasExportedData => SyncExportState.OldFormat,
        _ when Characters.Count == 0 && GuildRanks is null => SyncExportState.Guildless,
        _ => SyncExportState.Ready,
    };
}

public enum SyncExportState
{
    NoFile,
    OldFormat,
    Guildless,
    Ready,
}

public sealed record GuildRanks(
    string Realm,
    string Guild,
    DateTimeOffset? ObservedAt,
    IReadOnlyDictionary<int, string> Ranks);

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
    [property: JsonPropertyName("recipes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, ProfessionRecipes>? Recipes,
    [property: JsonPropertyName("fp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Fp = null);

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
    [property: JsonPropertyName("characters")] IReadOnlyList<CharacterSyncEntry> Characters,
    [property: JsonPropertyName("catalogue"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, ProfessionCatalogue>? Catalogue = null,
    [property: JsonPropertyName("guildRanks"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GuildRanksSync? GuildRanks = null);

public sealed record GuildRanksSync(
    [property: JsonPropertyName("realm")] string Realm,
    [property: JsonPropertyName("guild")] string Guild,
    [property: JsonPropertyName("observedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ObservedAt,
    [property: JsonPropertyName("ranks")] IReadOnlyDictionary<string, string> Ranks);

public sealed record ProfessionCatalogue(
    [property: JsonPropertyName("scannedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ScannedAt,
    [property: JsonPropertyName("list"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CatalogueRecipe>? List);

public sealed record CatalogueRecipe(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("recipeId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RecipeId,
    [property: JsonPropertyName("header"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Header,
    [property: JsonPropertyName("itemId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ItemId,
    [property: JsonPropertyName("tools"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tools,
    [property: JsonPropertyName("reagents"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProfessionReagent>? Reagents);

public sealed record RecipeCatalogueResponse(
    [property: JsonPropertyName("catalogue")] IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>> Catalogue);

public sealed record DirectoryPerson(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("main_guid")] string? MainGuid);

public sealed record DirectoryCharacter(
    [property: JsonPropertyName("guid")] string CharacterGuid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("class_id")] int ClassId,
    [property: JsonPropertyName("linked_user_id")] string? LinkedUserId);

public sealed record DirectorySkill(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("max_rank")] int MaxRank,
    [property: JsonPropertyName("secondary")] bool Secondary);

public sealed record DirectoryReagent(
    [property: JsonPropertyName("item_id")] int? ItemId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int? Count);

public sealed record DirectoryRecipe(
    [property: JsonPropertyName("recipe_id")] int RecipeId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("difficulty")] string? Difficulty,
    [property: JsonPropertyName("header")] string? Header,
    [property: JsonPropertyName("item_id")] int? ItemId,
    [property: JsonPropertyName("tools")] string? Tools,
    [property: JsonPropertyName("reagents")] IReadOnlyList<DirectoryReagent>? Reagents);

public sealed record DirectoryProfessions(
    [property: JsonPropertyName("guid")] string CharacterGuid,
    [property: JsonPropertyName("skills")] IReadOnlyList<DirectorySkill> Skills,
    [property: JsonPropertyName("recipes")] IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>> Recipes);

public sealed record GuildDirectory(
    [property: JsonPropertyName("people")] IReadOnlyList<DirectoryPerson> People,
    [property: JsonPropertyName("characters")] IReadOnlyList<DirectoryCharacter> Characters,
    [property: JsonPropertyName("professions")] IReadOnlyList<DirectoryProfessions> Professions,
    [property: JsonPropertyName("catalogue")] IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>> Catalogue);

public static class GuildDirectoryMapping
{
    public static IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>> ToCatalogue(GuildDirectory directory) =>
        directory.Catalogue.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<CatalogueRecipe>)[.. kv.Value.Select(ToCatalogueRecipe)],
            StringComparer.Ordinal);

    private static CatalogueRecipe ToCatalogueRecipe(DirectoryRecipe recipe) => new(
        recipe.Name,
        recipe.RecipeId,
        recipe.Header,
        recipe.ItemId,
        recipe.Tools,
        recipe.Reagents?.Select(r => new ProfessionReagent(r.Name, r.ItemId, r.Count)).ToList());
}

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

    public static GuildRanksSync ToSync(GuildRanks ranks) => new(
        ranks.Realm,
        ranks.Guild,
        ranks.ObservedAt?.ToUnixTimeSeconds(),
        ranks.Ranks.ToDictionary(entry => entry.Key.ToString(CultureInfo.InvariantCulture), entry => entry.Value, StringComparer.Ordinal));

    public static IReadOnlyList<CharacterObservation> FilterToProfessionsOnly(SavedVariablesSnapshot snapshot) =>
        [.. snapshot.Characters.Where(c => snapshot.Professions.ContainsKey(c.CharacterGuid))];

    // observedAt and scannedAt are restamped on every roster rebuild, so they are left out or every /reload would push unchanged data.
    public static string Fingerprint(
        IReadOnlyList<CharacterObservation> characters,
        IReadOnlyDictionary<string, CharacterProfessions> professions,
        IReadOnlyDictionary<string, ProfessionCatalogue>? catalogue = null,
        GuildRanks? guildRanks = null)
    {
        var canonicalProfessions = professions.ToDictionary(
            entry => entry.Key,
            entry => entry.Value with
            {
                ObservedAt = null,
                Recipes = entry.Value.Recipes is null ? null : Sorted(entry.Value.Recipes, recipes => recipes with { ScannedAt = null }),
            },
            StringComparer.Ordinal);
        var canonical = new CharacterSyncRequest(
            string.Empty,
            string.Empty,
            [.. characters.OrderBy(c => c.CharacterGuid, StringComparer.Ordinal)
                .Select(c => ToEntry(c with { ObservedAt = null }, canonicalProfessions))],
            catalogue is null or { Count: 0 } ? null : Sorted(catalogue, entry => entry with { ScannedAt = null }),
            guildRanks is null ? null : ToSync(guildRanks) with { ObservedAt = null });
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical, CompanionJsonContext.Default.CharacterSyncRequest)));
    }

    private static SortedDictionary<string, T> Sorted<T>(IReadOnlyDictionary<string, T> source, Func<T, T> canonicalise) =>
        new(source.ToDictionary(entry => entry.Key, entry => canonicalise(entry.Value), StringComparer.Ordinal), StringComparer.Ordinal);
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

public sealed record SyncMe(string Id, string? Role, IReadOnlyList<string> Features);

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

    public IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>> Catalogue { get; init; } = new Dictionary<string, IReadOnlyList<CatalogueRecipe>>();

    public SyncMe? Me { get; init; }

    public GuildDirectory? Directory { get; init; }
}

