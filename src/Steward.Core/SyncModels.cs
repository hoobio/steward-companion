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
    bool HasAccountData = false,
    int OutdatedProfessions = 0)
{
    public IReadOnlyDictionary<string, CharacterGear> Gear { get; init; } = new Dictionary<string, CharacterGear>();

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
    IReadOnlyDictionary<int, string> Ranks,
    string? RealmName = null,
    string? Fp = null,
    string? AddonVersion = null);

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
    DateTimeOffset? ObservedAt,
    string? RealmName = null,
    int? Gender = null,
    string? Fp = null,
    string? AddonVersion = null);

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
    [property: JsonPropertyName("professions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterProfessions? Professions = null,
    [property: JsonPropertyName("realmName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RealmName = null,
    [property: JsonPropertyName("gender"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Gender = null,
    [property: JsonPropertyName("gear"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterGear? Gear = null,
    [property: JsonPropertyName("fp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Fp = null,
    [property: JsonPropertyName("addonVersion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddonVersion = null);

public static class ProfessionsSchema
{
    public const int Current = 2;
}

public static class GearSchema
{
    public const int Current = 1;
}

public sealed record CharacterGear(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("observedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ObservedAt,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("equipped")] IReadOnlyDictionary<string, GearEntry> Equipped,
    [property: JsonPropertyName("bags")] IReadOnlyList<GearEntry> Bags,
    [property: JsonPropertyName("bank")] GearBank? Bank,
    [property: JsonPropertyName("fp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Fp,
    [property: JsonPropertyName("addonVersion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddonVersion = null);

public sealed record GearBank(
    [property: JsonPropertyName("observedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ObservedAt,
    [property: JsonPropertyName("items")] IReadOnlyList<GearEntry> Items);

public sealed record GearEntry(
    [property: JsonPropertyName("link")] string Link,
    [property: JsonPropertyName("itemID")] int ItemId,
    [property: JsonPropertyName("quality")] int Quality,
    [property: JsonPropertyName("enchantID"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EnchantId = null,
    [property: JsonPropertyName("suffixID"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SuffixId = null,
    [property: JsonPropertyName("count"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Count = null,
    [property: JsonPropertyName("equipLoc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EquipLoc = null,
    [property: JsonPropertyName("ilvl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Ilvl = null);

public sealed record CharacterProfessions(
    [property: JsonPropertyName("observedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ObservedAt,
    [property: JsonPropertyName("skills"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProfessionSkill>? Skills,
    [property: JsonPropertyName("recipes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyList<int>>? Recipes,
    [property: JsonPropertyName("fp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Fp = null,
    [property: JsonPropertyName("schema")] int Schema = ProfessionsSchema.Current,
    [property: JsonPropertyName("addonVersion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddonVersion = null);

public sealed record ProfessionSkill(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rank"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Rank,
    [property: JsonPropertyName("maxRank"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxRank,
    [property: JsonPropertyName("secondary"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Secondary);

public sealed record ProfessionReagent(
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
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
    [property: JsonPropertyName("ranks")] IReadOnlyDictionary<string, string> Ranks,
    [property: JsonPropertyName("realmName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RealmName = null,
    [property: JsonPropertyName("fp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Fp = null,
    [property: JsonPropertyName("addonVersion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddonVersion = null);

public sealed record ProfessionCatalogue(
    [property: JsonPropertyName("scannedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ScannedAt,
    [property: JsonPropertyName("list"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CatalogueRecipe>? List,
    [property: JsonPropertyName("fp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Fp = null,
    [property: JsonPropertyName("addonVersion"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddonVersion = null);

public sealed record CatalogueRecipe(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("recipeId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RecipeId,
    [property: JsonPropertyName("header"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Header,
    [property: JsonPropertyName("itemId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ItemId,
    [property: JsonPropertyName("tools"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tools,
    [property: JsonPropertyName("reagents"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProfessionReagent>? Reagents,
    [property: JsonPropertyName("order"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Order = null,
    [property: JsonPropertyName("grey"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Grey = null,
    [property: JsonPropertyName("orangeTo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? OrangeTo = null,
    [property: JsonPropertyName("yellowFrom"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? YellowFrom = null,
    [property: JsonPropertyName("yellowTo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? YellowTo = null,
    [property: JsonPropertyName("greenFrom"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? GreenFrom = null);

public sealed record RecipeCatalogueResponse(
    [property: JsonPropertyName("catalogue")] IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>> Catalogue);

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
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("count")] int? Count);

public sealed record DirectoryRecipe(
    [property: JsonPropertyName("recipe_id")] int RecipeId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("difficulty")] string? Difficulty,
    [property: JsonPropertyName("header")] string? Header,
    [property: JsonPropertyName("item_id")] int? ItemId,
    [property: JsonPropertyName("tools")] string? Tools,
    [property: JsonPropertyName("reagents")] IReadOnlyList<DirectoryReagent>? Reagents,
    [property: JsonPropertyName("order")] int? Order = null,
    [property: JsonPropertyName("grey")] int? Grey = null,
    [property: JsonPropertyName("orange_to")] int? OrangeTo = null,
    [property: JsonPropertyName("yellow_from")] int? YellowFrom = null,
    [property: JsonPropertyName("yellow_to")] int? YellowTo = null,
    [property: JsonPropertyName("green_from")] int? GreenFrom = null);

public sealed record DirectoryProfessions(
    [property: JsonPropertyName("guid")] string CharacterGuid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("class_id")] int ClassId,
    [property: JsonPropertyName("skills")] IReadOnlyList<DirectorySkill> Skills,
    [property: JsonPropertyName("recipes")] IReadOnlyDictionary<string, IReadOnlyList<int>> Recipes);

public sealed record MemberRoster(
    [property: JsonPropertyName("people")] IReadOnlyList<DirectoryPerson> People,
    [property: JsonPropertyName("characters")] IReadOnlyList<DirectoryCharacter> Characters);

public sealed record MemberProfessions(
    [property: JsonPropertyName("professions")] IReadOnlyList<DirectoryProfessions> Professions,
    [property: JsonPropertyName("catalogue")] IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>> Catalogue);

public sealed record MemberCatalogue(
    [property: JsonPropertyName("catalogue")] IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>> Catalogue);

// Composed from whichever of the roster/professions routes the selected guild's features allow; never itself sent or received as JSON.
public sealed record SyncDirectory(
    IReadOnlyList<DirectoryPerson>? People,
    IReadOnlyList<DirectoryCharacter>? Characters,
    IReadOnlyList<DirectoryProfessions>? Professions);

public static class MemberCatalogueMapping
{
    public static IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>> ToCatalogue(
        IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>> catalogue) =>
        catalogue.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<CatalogueRecipe>)[.. kv.Value.Select(ToCatalogueRecipe)],
            StringComparer.Ordinal);

    private static CatalogueRecipe ToCatalogueRecipe(DirectoryRecipe recipe) => new(
        recipe.Name,
        recipe.RecipeId,
        recipe.Header,
        recipe.ItemId,
        recipe.Tools,
        recipe.Reagents?.Select(r => new ProfessionReagent(r.Name, r.ItemId, r.Count)).ToList(),
        recipe.Order,
        recipe.Grey,
        recipe.OrangeTo,
        recipe.YellowFrom,
        recipe.YellowTo,
        recipe.GreenFrom);
}

public sealed record CharacterSyncRejection(
    [property: JsonPropertyName("guid")] string CharacterGuid,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record CharacterSyncResponse(
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("rejected")] IReadOnlyList<CharacterSyncRejection> Rejected,
    [property: JsonPropertyName("replay")] bool Replay = false);

public sealed record CharacterPushOutcome(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("reason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonPropertyName("fingerprint"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Fingerprint = null);

public sealed record CharacterPushRecord(
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset PushedAt,
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("characters"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, CharacterPushOutcome>? Characters = null,
    [property: JsonPropertyName("catalogue_fingerprint"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CatalogueFingerprint = null);

public static class CharacterSyncRejectionCopy
{
    public const string NotLinkedReason = "not linked to you";
    public const string GuildNotAllowedReason = "guild not allowed";
    private const string FingerprintMissing = "professions fingerprint missing";
    private const string FingerprintMismatch = "professions integrity check failed";

    public static string Describe(string reason) => reason switch
    {
        NotLinkedReason => "Not linked to you in the guild roster yet: ask an officer",
        FingerprintMissing or FingerprintMismatch => "Changed outside the game, not sent",
        GuildNotAllowedReason => "Not in a WoW guild this server syncs",
        _ => reason,
    };

    public static string DescribeNotLinked(
        string characterGuid,
        IReadOnlyList<DirectoryCharacter>? rosterCharacters,
        IReadOnlyList<DirectoryPerson>? rosterPeople,
        string? myUserId)
    {
        if (rosterCharacters is null)
        {
            return "Not linked to you";
        }

        var linkedUserId = rosterCharacters.FirstOrDefault(c => c.CharacterGuid == characterGuid)?.LinkedUserId;
        if (linkedUserId is null)
        {
            return "Not linked to a Discord account yet: ask an officer";
        }

        if (linkedUserId == myUserId)
        {
            return "Linked to you";
        }

        var name = rosterPeople?.FirstOrDefault(p => p.Id == linkedUserId)?.Name;
        return name is null ? "Linked to another Discord account" : $"Linked to {name} in the guild roster";
    }
}

public static class ProfessionsSkillSummary
{
    public static string Format(IReadOnlyList<ProfessionSkill>? skills) =>
        skills is null or { Count: 0 }
            ? string.Empty
            : string.Join(", ", skills.Where(skill => skill.Secondary != true).Select(Describe));

    private static string Describe(ProfessionSkill skill) =>
        skill.Rank is { } rank && skill.MaxRank is { } maxRank ? $"{skill.Name} {rank}/{maxRank}" : skill.Name;
}

public sealed record CharacterSyncBatch(
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("batch_id")] string BatchId);

public sealed record CharacterPushScope(
    IReadOnlyList<CharacterObservation> Characters,
    GuildRanks? GuildRanks,
    string? Fingerprint);

public sealed record CharacterPushTarget(string GuildId, bool ProfessionsOnly, IReadOnlyList<string>? SyncGuildNames);

public sealed record CharacterPushRoute(string GuildId, bool ProfessionsOnly, CharacterPushScope Scope);

public static class CharacterPushRouting
{
    public static IReadOnlyList<CharacterPushRoute> Route(
        SavedVariablesSnapshot snapshot,
        IReadOnlyList<CharacterPushTarget> targets,
        string? selectedGuildId)
    {
        var selected = targets.FirstOrDefault(t => t.GuildId == selectedGuildId);
        IEnumerable<CharacterPushTarget> routed = selected is { SyncGuildNames: null }
            ? [selected]
            : targets.Where(t => t.SyncGuildNames is { Count: > 0 });
        return [.. routed
            .Select(t => new CharacterPushRoute(t.GuildId, t.ProfessionsOnly, CharacterSyncMapping.Scope(snapshot, t.ProfessionsOnly, t.SyncGuildNames)))
            .Where(r => r.Scope.Fingerprint is not null)];
    }

    public static ProfessionsCharacterState? StateOf(CharacterPushOutcome? outcome, bool professionsOnly, string? professionsFingerprint, bool batchCurrent) =>
        outcome is { Accepted: false, Reason: CharacterSyncRejectionCopy.GuildNotAllowedReason }
            ? (professionsOnly ? string.Equals(outcome.Fingerprint, professionsFingerprint, StringComparison.Ordinal) : batchCurrent)
                ? null
                : ProfessionsCharacterState.Pending
        : professionsOnly ? ProfessionsPushSelection.StateOf(outcome, professionsFingerprint ?? string.Empty)
        : !batchCurrent || outcome is null ? ProfessionsCharacterState.Pending
        : outcome.Accepted ? ProfessionsCharacterState.Synced
        : ProfessionsCharacterState.Rejected;

    public static ProfessionsCharacterState? Combine(IEnumerable<ProfessionsCharacterState?> states)
    {
        var sent = states.OfType<ProfessionsCharacterState>().ToList();
        return sent.Count == 0 ? null
            : sent.Contains(ProfessionsCharacterState.Pending) ? ProfessionsCharacterState.Pending
            : sent.Contains(ProfessionsCharacterState.Rejected) ? ProfessionsCharacterState.Rejected
            : ProfessionsCharacterState.Synced;
    }
}

public static class CharacterSyncMapping
{
    public static CharacterSyncEntry ToEntry(
        CharacterObservation observation,
        IReadOnlyDictionary<string, CharacterProfessions> professions,
        IReadOnlyDictionary<string, CharacterGear>? gear = null) => new(
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
        professions.GetValueOrDefault(observation.CharacterGuid),
        observation.RealmName,
        observation.Gender,
        gear?.GetValueOrDefault(observation.CharacterGuid),
        observation.Fp,
        observation.AddonVersion);

    public static GuildRanksSync ToSync(GuildRanks ranks) => new(
        ranks.Realm,
        ranks.Guild,
        ranks.ObservedAt?.ToUnixTimeSeconds(),
        ranks.Ranks.ToDictionary(entry => entry.Key.ToString(CultureInfo.InvariantCulture), entry => entry.Value, StringComparer.Ordinal),
        ranks.RealmName,
        ranks.Fp,
        ranks.AddonVersion);

    public static IReadOnlyList<CharacterObservation> FilterToProfessionsOnly(SavedVariablesSnapshot snapshot) =>
        [.. snapshot.Characters.Where(c => snapshot.Professions.ContainsKey(c.CharacterGuid))];

    public static bool IsAllowedGuild(string? guild, IReadOnlyList<string>? allowedGuilds) =>
        allowedGuilds is null
        || allowedGuilds.Any(allowed => string.Equals(allowed.Trim(), guild?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static CharacterPushScope Scope(SavedVariablesSnapshot snapshot, bool professionsOnly, IReadOnlyList<string>? allowedGuilds)
    {
        var candidates = professionsOnly ? FilterToProfessionsOnly(snapshot) : snapshot.Characters;
        var characters = allowedGuilds is null ? candidates : [.. candidates.Where(c => IsAllowedGuild(c.Guild, allowedGuilds))];
        var guildRanks = professionsOnly || snapshot.GuildRanks is null || !IsAllowedGuild(snapshot.GuildRanks.Guild, allowedGuilds)
            ? null
            : snapshot.GuildRanks;
        var fingerprint = !snapshot.HasAccountData || (allowedGuilds is not null && characters.Count == 0) ? null
            : allowedGuilds is null && !professionsOnly ? snapshot.CharactersFingerprint
            : Fingerprint(characters, snapshot.Professions, snapshot.Catalogue, guildRanks, snapshot.Gear);
        return new CharacterPushScope(characters, guildRanks, fingerprint);
    }

    public static string? EffectiveLinkedUserId(CharacterObservation observation, IReadOnlyList<DirectoryCharacter>? rosterCharacters)
    {
        var pin = rosterCharacters?.FirstOrDefault(r => r.CharacterGuid == observation.CharacterGuid);
        return pin is not null ? pin.LinkedUserId : observation.LinkedUserId;
    }

    public static bool IsOwnCharacter(CharacterObservation observation, IReadOnlyList<DirectoryCharacter>? rosterCharacters, string? myUserId) =>
        myUserId is not null
        && string.Equals(EffectiveLinkedUserId(observation, rosterCharacters), myUserId, StringComparison.Ordinal);

    // observedAt, scannedAt, every fp and addonVersion are left out: each fp signs its timestamp, so keeping them would push on every restamp.
    public static string Fingerprint(
        IReadOnlyList<CharacterObservation> characters,
        IReadOnlyDictionary<string, CharacterProfessions> professions,
        IReadOnlyDictionary<string, ProfessionCatalogue>? catalogue = null,
        GuildRanks? guildRanks = null,
        IReadOnlyDictionary<string, CharacterGear>? gear = null)
    {
        var canonicalProfessions = professions.ToDictionary(entry => entry.Key, entry => Canonical(entry.Value), StringComparer.Ordinal);
        var canonicalGear = gear?.ToDictionary(entry => entry.Key, entry => Canonical(entry.Value), StringComparer.Ordinal);
        var canonical = new CharacterSyncRequest(
            string.Empty,
            string.Empty,
            [.. characters.OrderBy(c => c.CharacterGuid, StringComparer.Ordinal)
                .Select(c => ToEntry(c with { ObservedAt = null, Fp = null, AddonVersion = null }, canonicalProfessions, canonicalGear))],
            catalogue is null or { Count: 0 } ? null : Sorted(catalogue, entry => entry with { ScannedAt = null, Fp = null, AddonVersion = null }),
            guildRanks is null ? null : ToSync(guildRanks) with { ObservedAt = null, Fp = null, AddonVersion = null });
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical, CompanionJsonContext.Default.CharacterSyncRequest)));
    }

    public static string ProfessionsFingerprint(CharacterProfessions professions, CharacterGear? gear = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Canonical(professions), CompanionJsonContext.Default.CharacterProfessions);
        if (gear is not null)
        {
            bytes = [.. bytes, .. JsonSerializer.SerializeToUtf8Bytes(Canonical(gear), CompanionJsonContext.Default.CharacterGear)];
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static string CatalogueFingerprint(IReadOnlyDictionary<string, ProfessionCatalogue> catalogue) =>
        Fingerprint([], new Dictionary<string, CharacterProfessions>(), catalogue);

    private static CharacterProfessions Canonical(CharacterProfessions professions) => professions with
    {
        ObservedAt = null,
        Fp = null,
        AddonVersion = null,
        Recipes =professions.Recipes is null ? null : Sorted(professions.Recipes, ids => ids),
    };

    private static CharacterGear Canonical(CharacterGear gear) => gear with { ObservedAt = null, Fp = null, AddonVersion = null };

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

public enum ProfessionsCharacterState
{
    Synced,
    Pending,
    Rejected,
}

public sealed record ProfessionsPushPlan(
    IReadOnlyList<CharacterObservation> Characters,
    IReadOnlyDictionary<string, string> Fingerprints,
    string? CatalogueFingerprint,
    bool SendCatalogue)
{
    public bool HasWork => Characters.Count > 0 || SendCatalogue;
}

public static class ProfessionsPushSelection
{
    public static ProfessionsPushPlan Select(
        IReadOnlyList<CharacterObservation> characters,
        IReadOnlyDictionary<string, CharacterProfessions> professions,
        IReadOnlyDictionary<string, ProfessionCatalogue> catalogue,
        CharacterPushRecord? last,
        IReadOnlyList<DirectoryCharacter>? rosterCharacters,
        string? myUserId,
        bool force,
        IReadOnlyDictionary<string, CharacterGear>? gear = null)
    {
        var fingerprints = characters.ToDictionary(
            c => c.CharacterGuid,
            c => CharacterSyncMapping.ProfessionsFingerprint(professions[c.CharacterGuid], gear?.GetValueOrDefault(c.CharacterGuid)),
            StringComparer.Ordinal);
        var catalogueFingerprint = catalogue.Count == 0 ? null : CharacterSyncMapping.CatalogueFingerprint(catalogue);
        var selected = characters
            .Where(c => force || NeedsSend(
                last?.Characters?.GetValueOrDefault(c.CharacterGuid),
                fingerprints[c.CharacterGuid],
                myUserId is not null && rosterCharacters?.Any(r => r.CharacterGuid == c.CharacterGuid && r.LinkedUserId == myUserId) == true))
            .ToList();
        var sendCatalogue = catalogueFingerprint is not null
            && (force || !string.Equals(catalogueFingerprint, last?.CatalogueFingerprint, StringComparison.Ordinal));
        return new ProfessionsPushPlan(selected, fingerprints, catalogueFingerprint, sendCatalogue);
    }

    public static IReadOnlyDictionary<string, CharacterPushOutcome> Merge(
        IReadOnlyDictionary<string, CharacterPushOutcome>? previous,
        ProfessionsPushPlan plan,
        IReadOnlyDictionary<string, string> rejections)
    {
        var sent = plan.Characters.Select(c => c.CharacterGuid).ToHashSet(StringComparer.Ordinal);
        var merged = new Dictionary<string, CharacterPushOutcome>(StringComparer.Ordinal);
        foreach (var (guid, fingerprint) in plan.Fingerprints)
        {
            if (sent.Contains(guid))
            {
                merged[guid] = rejections.TryGetValue(guid, out var reason)
                    ? new CharacterPushOutcome(false, reason, fingerprint)
                    : new CharacterPushOutcome(true, Fingerprint: fingerprint);
            }
            else if (previous?.GetValueOrDefault(guid) is { } kept)
            {
                merged[guid] = kept;
            }
        }

        return merged;
    }

    public static ProfessionsCharacterState StateOf(CharacterPushOutcome? outcome, string fingerprint) => outcome switch
    {
        null => ProfessionsCharacterState.Pending,
        { Accepted: false } => ProfessionsCharacterState.Rejected,
        _ when !string.Equals(outcome.Fingerprint, fingerprint, StringComparison.Ordinal) => ProfessionsCharacterState.Pending,
        _ => ProfessionsCharacterState.Synced,
    };

    // An officer fixing the roster link is the only change outside the character's own data that can turn a not-linked rejection into an accept.
    private static bool NeedsSend(CharacterPushOutcome? outcome, string fingerprint, bool linkedToMe) => outcome switch
    {
        null => true,
        _ when !string.Equals(outcome.Fingerprint, fingerprint, StringComparison.Ordinal) => true,
        { Accepted: true } => false,
        _ => linkedToMe && outcome.Reason == CharacterSyncRejectionCopy.NotLinkedReason,
    };
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

    public SyncDirectory? Directory { get; init; }
}

