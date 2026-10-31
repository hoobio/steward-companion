using System.Buffers.Text;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Steward.Core;

public static class AddonSettingsPayload
{
    public const string Prefix = "v1.";

    public const string Scheme = "steward";

    public const string ImportLink = "steward://import";

    public const string WebsiteUrl = "https://guild.hoobi.io/addon-settings";

    public const int MaxLinkLength = 64 * 1024;

    public const int MaxExportUrlLength = 32_000;

    public const string TooLarge = "Too much to export at once; choose one account";

    public const string NotALink = "This is not an import link from the Steward website";

    private const int MaxJsonBytes = 4 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Encode(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(Encoding.UTF8.GetBytes(json));
        }

        return Prefix + Base64Url.EncodeToString(buffer.ToArray());
    }

    public static string Decode(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException(NotALink);
        }

        byte[] compressed;
        try
        {
            compressed = Base64Url.DecodeFromChars(payload.AsSpan(Prefix.Length));
        }
        catch (FormatException ex)
        {
            throw new FormatException(NotALink, ex);
        }

        try
        {
            using var deflate = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = deflate.Read(chunk)) > 0)
            {
                output.Write(chunk, 0, read);
                if (output.Length > MaxJsonBytes)
                {
                    throw new FormatException("The import is too large");
                }
            }

            return StrictUtf8.GetString(output.ToArray());
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or ArgumentException)
        {
            throw new FormatException(NotALink, ex);
        }
    }

    public static string FromLink(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var link = text.Trim();
        if (link.Length > MaxLinkLength)
        {
            throw new FormatException("The link is too long to import");
        }

        if (link.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            // Packaged activation hands over Uri.AbsoluteUri, which adds a slash after the host: steward://import/#v1...
            var start = link.StartsWith(ImportLink + "#", StringComparison.OrdinalIgnoreCase) ? ImportLink.Length + 1
                : link.StartsWith(ImportLink + "/#", StringComparison.OrdinalIgnoreCase) ? ImportLink.Length + 2
                : throw new FormatException(NotALink);
            link = link[start..];
        }

        if (link.Contains('%', StringComparison.Ordinal))
        {
            link = Uri.UnescapeDataString(link);
        }

        return link.StartsWith(Prefix, StringComparison.Ordinal) ? link : throw new FormatException(NotALink);
    }

    public static bool IsImportLink(string? link) => link?.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase) == true;

    public static string ExportUrl(string payload, string? view = null) =>
        view is null ? $"{WebsiteUrl}#{payload}" : $"{WebsiteUrl}?view={Uri.EscapeDataString(view)}#{payload}";
}

public sealed record SettingsSource(string AddonId, string Folder, string? Version, SettingsSchema? Schema);

public sealed class SettingsAccount
{
    public const string CoreGlobal = "HoobiScriptsDB";

    public const string ActionBarsGlobal = "HoobiScripts_ActionBarsDB";

    public const string NoOwnerNotice = "No installed addon keeps this setting";

    public const string UpdateStewardNotice = "Update Steward to change this setting";

    private readonly Dictionary<string, IReadOnlyDictionary<string, LuaValue>?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    public SettingsAccount(string flavourPath, string addOnsPath, string account, string? productCode, IReadOnlyDictionary<string, SavedVariableOwner?> owners)
    {
        FlavourPath = flavourPath;
        AddOnsPath = addOnsPath;
        Account = account;
        ProductCode = productCode;
        Owners = owners;
        AccountFolder = AddonSettings.AccountFolder(flavourPath, account);
        CoreFile = AccountFileOf(CoreGlobal);
        Core = (CoreFile is null ? null : Read(CoreFile)) ?? new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        Characters = AddonSettings.CharactersOn(AddonSettings.Characters(Core, CoreGlobal), productCode);
        KnownRealms = AddonSettings.KnownRealms(AccountFolder, Characters, Path.Combine(flavourPath, "Logs", "Connection.log"));
    }

    public string FlavourPath { get; }

    public string AddOnsPath { get; }

    public string Account { get; }

    public string? ProductCode { get; }

    public string AccountFolder { get; }

    public IReadOnlyDictionary<string, SavedVariableOwner?> Owners { get; }

    public string? CoreFile { get; }

    public IReadOnlyDictionary<string, LuaValue> Core { get; }

    public IReadOnlyList<string> Characters { get; }

    public IReadOnlyDictionary<string, string> KnownRealms { get; }

    public string? ClassOf(string? character) => AddonSettings.ClassOf(Core, character, CoreGlobal);

    public string? AccountFileOf(string global) =>
        Owners.GetValueOrDefault(global) is { PerCharacter: false } owner ? AddonSettings.FilePath(FlavourPath, Account, owner.Folder) : null;

    public string TitleOf(string global) => Owners.GetValueOrDefault(global)?.Title ?? global;

    public IReadOnlyDictionary<string, LuaValue>? Read(string file)
    {
        if (_files.TryGetValue(file, out var known))
        {
            return known;
        }

        try
        {
            return _files[file] = AddonSettings.Load(file);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            _unreadable[file] = $"Steward could not read {Path.GetFileName(file)}: {ex.Message}";
            return _files[file] = null;
        }
    }

    public string? UnreadableNotice(string file) => Read(file) is null ? _unreadable.GetValueOrDefault(file) : null;

    public (string? File, string? Notice) FileFor(SettingsRow row, string? character, bool knownCharacter = true)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (AddonSettings.OwnerOf(Owners, row) is not { } owner)
        {
            return (null, NoOwnerNotice);
        }

        string file;
        if (row.IsCharacterFile)
        {
            var folder = character is null ? null : AddonSettings.CharacterFolder(AccountFolder, character, KnownRealms);
            if (folder is null)
            {
                return (null, "Log in to this character once first");
            }

            file = AddonSettings.CharacterFilePath(folder, owner.Folder);
        }
        else
        {
            file = AddonSettings.FilePath(FlavourPath, Account, owner.Folder);
            if (row.Scope == "character" && !knownCharacter)
            {
                return (null, "Steward does not know this character");
            }
        }

        return File.Exists(file) ? (file, UnreadableNotice(file)) : (file, AddonSettings.NotLoggedInNotice(owner.Title));
    }

    public string? Refusal(SettingsSchema schema, SettingsRow row)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(row);
        if (schema.IsReadOnly || !row.IsKnownType)
        {
            return UpdateStewardNotice;
        }

        if (!row.IsSetting)
        {
            return "Not a setting";
        }

        if (row.Type == "dropdown" && row.DynamicOptions)
        {
            return "Set in the game";
        }

        return AddonSettings.AnyInstalled(AddOnsPath, row.LockedWhenLoaded)
            ? $"Off while {string.Join(" or ", row.LockedWhenLoaded!)} is installed"
            : null;
    }
}

public static class AddonSettingsExport
{
    public static string Build(SettingsAccount account, IReadOnlyList<SettingsSource> addons, string installName, long exportedAt)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(addons);
        var installed = Directory.Exists(account.AddOnsPath)
            ? Directory.EnumerateDirectories(account.AddOnsPath).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        var values = new JsonObject();
        foreach (var source in addons.Where(source => source.Schema is not null))
        {
            if (Values(account, source.Schema!) is { Count: > 0 } addonValues)
            {
                values[source.Schema!.Folder] = addonValues;
            }
        }

        var payload = new JsonObject
        {
            ["v"] = 1,
            ["product"] = account.ProductCode,
            ["install"] = installName,
            ["account"] = account.Account,
            ["exportedAt"] = exportedAt,
            ["addons"] = new JsonArray([.. addons.Select(source =>
            {
                var entry = new JsonObject { ["id"] = source.AddonId, ["folder"] = source.Folder, ["version"] = source.Version };
                if (source.Schema is null)
                {
                    entry["unavailable"] = true;
                }

                return (JsonNode)entry;
            })]),
            ["installed"] = new JsonArray([.. installed.Select(folder => (JsonNode)folder)]),
            ["realms"] = new JsonArray([.. account.KnownRealms.OrderBy(realm => realm.Key, StringComparer.OrdinalIgnoreCase)
                .Select(realm => (JsonNode)new JsonObject { ["name"] = realm.Key, ["id"] = realm.Value })]),
            ["characters"] = new JsonArray([.. account.Characters.Select(character => (JsonNode)new JsonObject
            {
                ["key"] = character,
                ["class"] = account.ClassOf(character) ?? "",
            })]),
            ["values"] = values,
            ["plans"] = new JsonObject
            {
                ["talents"] = SettingsPlans.ExportTalents(account),
                ["talentSpells"] = SettingsPlans.ExportTalentSpells(account),
                ["actionBars"] = SettingsPlans.ExportActionBars(account),
                ["spells"] = SettingsPlans.ExportSpells(account),
            },
        };
        if (AddonSettings.UnrecordedCharacters(account.AccountFolder, account.KnownRealms, account.Characters) is { Count: > 0 } unrecorded)
        {
            payload["unrecorded"] = new JsonArray([.. unrecorded.Select(entry => (JsonNode)new JsonObject
            {
                ["first"] = entry.First,
                ["last"] = entry.Last,
                ["realm"] = entry.Realm,
            })]);
        }

        return payload.ToJsonString(Compact);
    }

    // The payload travels base64url-encoded, so escaping non-ASCII and HTML characters only makes the link longer.
    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static JsonObject Values(SettingsAccount account, SettingsSchema schema)
    {
        var result = new JsonObject();
        void Put(string scope, string? owner, string key, LuaValue? value)
        {
            if (value is null || ToJson(value) is not { } json)
            {
                return;
            }

            var section = result[scope] as JsonObject ?? (JsonObject)(result[scope] = new JsonObject());
            if (owner is not null)
            {
                section = section[owner] as JsonObject ?? (JsonObject)(section[owner] = new JsonObject());
            }

            section.TryAdd(key, json);
        }

        foreach (var row in (schema.Pages ?? []).SelectMany(page => page.Sections ?? []).SelectMany(section => section.Rows ?? []).Where(row => row.IsSetting))
        {
            var scope = row.Scope ?? "account";
            if (scope == "class")
            {
                if (Globals(account, row, null) is { } globals)
                {
                    foreach (var token in AddonSettings.ClassTokens)
                    {
                        Put(scope, token, row.Key!, AddonSettings.Stored(globals, row, token, token));
                    }
                }
            }
            else if (row.IsCharacter)
            {
                foreach (var character in account.Characters)
                {
                    if (Globals(account, row, character) is { } globals)
                    {
                        Put(scope, character, row.Key!, AddonSettings.Stored(globals, row, character, account.ClassOf(character)));
                    }
                }
            }
            else if (Globals(account, row, null) is { } globals)
            {
                Put(scope, null, row.Key!, AddonSettings.Stored(globals, row, null));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, LuaValue>? Globals(SettingsAccount account, SettingsRow row, string? character) =>
        account.FileFor(row, character) is { File: { } file } && File.Exists(file) ? account.Read(file) : null;

    internal static JsonNode? ToJson(LuaValue value) => value.Kind switch
    {
        LuaKind.Boolean => JsonValue.Create(value.Boolean),
        LuaKind.Number => JsonValue.Create(value.Number),
        LuaKind.Text => JsonValue.Create(value.Text),
        _ => null,
    };
}

public static class SettingsPlans
{
    public const int MaxTalentPoints = 51;

    public const int MaxSlot = 180;

    public const string AccountTarget = "account";

    public static readonly IReadOnlySet<string> GeneratedMacros = new HashSet<string>(
        ["PowerShift", "HoobiTargets", "HoobiWater", "HoobiFood", "HoobiBandage", "HoobiHealthPotion", "HoobiManaPotion"], StringComparer.Ordinal);

    private static readonly HashSet<string> ArrayKeys = new(["variants", "spells", "list"], StringComparer.Ordinal);

    public static JsonObject ExportTalents(SettingsAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var result = new JsonObject();
        if (TalentPlans(account) is not { } plans)
        {
            return result;
        }

        foreach (var entry in plans.Table.Where(entry => entry.Key is { Kind: LuaKind.Text } && entry.Value.Kind == LuaKind.Table))
        {
            var groups = new JsonObject();
            foreach (var (index, value) in Indexed(entry.Value))
            {
                if (value.Kind == LuaKind.Table)
                {
                    groups[index.ToString(CultureInfo.InvariantCulture)] = new JsonArray([.. Talents(value).Select(id => (JsonNode)id)]);
                }
            }

            result[AddonSettings.FromFile(entry.Key!.Text!)] = groups;
        }

        return result;
    }

    public static JsonObject ExportTalentSpells(SettingsAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var result = new JsonObject();
        foreach (var entry in TalentPlanSpells(account)?.Table.Where(entry => entry.Key is { Kind: LuaKind.Text } && entry.Value.Kind == LuaKind.Table) ?? [])
        {
            var groups = new JsonObject();
            foreach (var (group, value) in Indexed(entry.Value).Where(group => group.Value.Kind == LuaKind.Table))
            {
                if (TalentStamp(value) is { Count: > 0 } stamp)
                {
                    groups[group.ToString(CultureInfo.InvariantCulture)] = new JsonObject([.. stamp
                        .Select(spell => KeyValuePair.Create(spell.Key.ToString(CultureInfo.InvariantCulture), (JsonNode?)spell.Value))]);
                }
            }

            if (groups.Count > 0)
            {
                result[AddonSettings.FromFile(entry.Key!.Text!)] = groups;
            }
        }

        return result;
    }

    public static SortedDictionary<long, long> TalentStamp(LuaValue? group) =>
        new(Indexed(group ?? LuaValue.Nil).Where(node => node.Value.Kind == LuaKind.Number && node.Value.Number >= 1).DistinctBy(node => node.Index).ToDictionary(node => node.Index, node => (long)node.Value.Number));

    public static JsonObject ExportActionBars(SettingsAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var result = new JsonObject();
        if (ActionBarsDb(account) is not { } db)
        {
            return result;
        }

        var classes = new JsonObject();
        foreach (var entry in db.GetTable("plans")?.Table.Where(entry => entry.Key is { Kind: LuaKind.Text } && entry.Value.Kind == LuaKind.Table) ?? [])
        {
            classes[AddonSettings.FromFile(entry.Key!.Text!)] = PlanToJson(entry.Value);
        }

        result["classes"] = classes;
        if (db.GetTable(AccountTarget) is { } accountPlan)
        {
            result[AccountTarget] = PlanToJson(accountPlan);
        }

        return result;
    }

    public static JsonObject ExportSpells(SettingsAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var result = new JsonObject();
        foreach (var entry in SpellPlans(account)?.Table.Where(entry => entry.Key is { Kind: LuaKind.Text } && entry.Value.Kind == LuaKind.Table) ?? [])
        {
            result[AddonSettings.FromFile(entry.Key!.Text!)] = new JsonObject([.. Spells(entry.Value)
                .Select(spell => KeyValuePair.Create(spell.Key, (JsonNode?)spell.Value))]);
        }

        return result;
    }

    public static SortedDictionary<string, long> Spells(LuaValue? plan)
    {
        var result = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in plan?.Table ?? [])
        {
            if (entry.Key is { Kind: LuaKind.Text } key && entry.Value.Kind == LuaKind.Number && entry.Value.Number != 0)
            {
                result[AddonSettings.FromFile(key.Text!)] = (long)entry.Value.Number;
            }
        }

        return result;
    }

    internal static LuaValue? SpellPlans(SettingsAccount account) => account.Core.GetValueOrDefault(SettingsAccount.CoreGlobal)?.GetTable("spellPlans");

    internal static LuaValue? TalentPlans(SettingsAccount account) => account.Core.GetValueOrDefault(SettingsAccount.CoreGlobal)?.GetTable("talentPlans");

    internal static LuaValue? TalentPlanSpells(SettingsAccount account) => account.Core.GetValueOrDefault(SettingsAccount.CoreGlobal)?.GetTable("talentPlanSpells");

    internal static LuaValue? ActionBarsDb(SettingsAccount account) =>
        account.AccountFileOf(SettingsAccount.ActionBarsGlobal) is { } file && File.Exists(file) && account.Read(file) is { } globals
            ? globals.GetValueOrDefault(SettingsAccount.ActionBarsGlobal) is { Kind: LuaKind.Table } db ? db : null
            : null;

    public static List<long> Talents(LuaValue group) =>
        [.. Indexed(group).OrderBy(entry => entry.Index).Where(entry => entry.Value.Kind == LuaKind.Number).Select(entry => (long)entry.Value.Number)];

    public static IEnumerable<(long Index, LuaValue Value)> Indexed(LuaValue table)
    {
        ArgumentNullException.ThrowIfNull(table);
        long position = 0;
        foreach (var entry in table.Table)
        {
            if (entry.Key is null)
            {
                position++;
                if (entry.Value.Kind != LuaKind.Nil)
                {
                    yield return (position, entry.Value);
                }
            }
            else if (entry.Key is { Kind: LuaKind.Number } key && key.Number == Math.Floor(key.Number) && entry.Value.Kind != LuaKind.Nil)
            {
                yield return ((long)key.Number, entry.Value);
            }
        }
    }

    public static LuaValue? GetIndex(LuaValue? table, long index) =>
        table is { Kind: LuaKind.Table } ? Indexed(table).LastOrDefault(entry => entry.Index == index).Value : null;

    public static JsonObject PlanToJson(LuaValue plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = new JsonObject();
        foreach (var (index, value) in Indexed(plan).OrderBy(entry => entry.Index))
        {
            result[index.ToString(CultureInfo.InvariantCulture)] = ToJson(value, null);
        }

        foreach (var entry in plan.Table.Where(entry => entry.Key is { Kind: LuaKind.Text }))
        {
            result[AddonSettings.FromFile(entry.Key!.Text!)] = ToJson(entry.Value, entry.Key.Text);
        }

        return result;
    }

    private static JsonNode? ToJson(LuaValue value, string? key)
    {
        if (value.Kind != LuaKind.Table)
        {
            return value.Kind == LuaKind.Text ? JsonValue.Create(AddonSettings.FromFile(value.Text!)) : AddonSettingsExport.ToJson(value);
        }

        var indexed = Indexed(value).OrderBy(entry => entry.Index).ToList();
        var named = value.Table.Where(entry => entry.Key is { Kind: LuaKind.Text }).ToList();
        if (named.Count == 0 && (indexed.Count > 0 || (key is not null && ArrayKeys.Contains(key))))
        {
            return new JsonArray([.. indexed.Select(entry => ToJson(entry.Value, null))]);
        }

        var result = new JsonObject();
        foreach (var (index, item) in indexed)
        {
            result[index.ToString(CultureInfo.InvariantCulture)] = ToJson(item, null);
        }

        foreach (var entry in named)
        {
            result[AddonSettings.FromFile(entry.Key!.Text!)] = ToJson(entry.Value, entry.Key.Text);
        }

        return result;
    }

    public static LuaValue PlanToLua(JsonObject plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var entries = new List<LuaEntry>();
        foreach (var (key, value) in plan.Where(entry => long.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .OrderBy(entry => long.Parse(entry.Key, CultureInfo.InvariantCulture)))
        {
            entries.Add(new LuaEntry(LuaValue.FromNumber(long.Parse(key, CultureInfo.InvariantCulture)), ToLua(value)));
        }

        entries.AddRange(plan.Where(entry => !long.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(entry => new LuaEntry(LuaValue.FromString(AddonSettings.ToFile(entry.Key)), ToLua(entry.Value))));
        return LuaValue.FromTable(entries);
    }

    private static LuaValue ToLua(JsonNode? node) => node switch
    {
        JsonObject obj => LuaValue.FromTable([.. obj.Select(entry => new LuaEntry(LuaValue.FromString(AddonSettings.ToFile(entry.Key)), ToLua(entry.Value)))]),
        JsonArray array => LuaValue.Array(array.Select(ToLua)),
        JsonValue value when value.GetValueKind() == JsonValueKind.String => LuaValue.FromString(AddonSettings.ToFile(value.GetValue<string>())),
        JsonValue value when value.GetValueKind() == JsonValueKind.Number => LuaValue.FromNumber(value.GetValue<double>()),
        JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False => LuaValue.FromBoolean(value.GetValue<bool>()),
        _ => LuaValue.Nil,
    };

    public static string Canonical(JsonNode? node)
    {
        var builder = new StringBuilder();
        Write(builder, node);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                builder.Append('{');
                foreach (var (key, value) in obj.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    builder.Append(JsonValue.Create(key).ToJsonString()).Append(':');
                    Write(builder, value);
                    builder.Append(',');
                }

                builder.Append('}');
                break;
            case JsonArray array:
                builder.Append('[');
                foreach (var item in array)
                {
                    Write(builder, item);
                    builder.Append(',');
                }

                builder.Append(']');
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                builder.Append(value.GetValue<double>().ToString("R", CultureInfo.InvariantCulture));
                break;
            case null:
                builder.Append("null");
                break;
            default:
                builder.Append(node.ToJsonString());
                break;
        }
    }

    public static LuaValue SetPath(LuaValue? table, IReadOnlyList<LuaValue> keys, LuaValue value, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (table is not null && table.Kind is not (LuaKind.Table or LuaKind.Nil))
        {
            throw new InvalidOperationException("A saved plan is not a table");
        }

        var key = keys[index];
        long position = 0;
        var entries = key.Kind == LuaKind.Number
            ? [.. (table?.Table ?? []).Select(entry => entry.Key is null ? new LuaEntry(LuaValue.FromNumber(++position), entry.Value) : entry)]
            : (table?.Table ?? []).ToList();
        var at = entries.FindIndex(entry => entry.Key is { } existing && existing.Kind == key.Kind
            && (key.Kind == LuaKind.Number ? existing.Number.Equals(key.Number) : string.Equals(existing.Text, key.Text, StringComparison.Ordinal)));
        var child = index == keys.Count - 1 ? value : SetPath(at >= 0 ? entries[at].Value : null, keys, value, index + 1);
        if (at >= 0)
        {
            entries[at] = entries[at] with { Value = child };
        }
        else
        {
            entries.Add(new LuaEntry(key, child));
        }

        return LuaValue.FromTable(entries);
    }

    public static string Summary(LuaValue? plan) => plan is null ? "No plan" : Summary(PlanToJson(plan));

    public static string Summary(JsonObject? plan)
    {
        if (plan is null)
        {
            return "No plan";
        }

        var slots = plan.Count(entry => long.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        var macros = (plan["macros"] as JsonObject)?.Count ?? 0;
        return $"{slots} {(slots == 1 ? "slot" : "slots")}, {macros} {(macros == 1 ? "macro" : "macros")}";
    }
}
