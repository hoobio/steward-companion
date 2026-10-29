using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Steward.Core;

public sealed record SettingsSchema(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("addon")] string? Addon,
    [property: JsonPropertyName("folder")] string Folder,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("pages")] IReadOnlyList<SettingsSchemaPage>? Pages)
{
    public const int KnownVersion = 1;

    public bool IsReadOnly => SchemaVersion > KnownVersion;

    public static SettingsSchema Parse(string json) =>
        JsonSerializer.Deserialize(json, CompanionJsonContext.Default.SettingsSchema) ?? throw new JsonException("The settings schema is empty");

    public string? Refusal(string folderName)
    {
        if (!string.Equals(Folder, folderName, StringComparison.OrdinalIgnoreCase))
        {
            return $"its folder {Folder ?? "(none)"} is not {folderName}";
        }

        foreach (var row in (Pages ?? []).SelectMany(page => page.Sections ?? []).SelectMany(section => section.Rows ?? []))
        {
            if (row.Path is not { Count: > 0 } path)
            {
                continue;
            }

            if (path[0] is not { } global || !AddonSettings.GlobalName().IsMatch(global))
            {
                return $"row {row.Key} names the global {path[0] ?? "(none)"}";
            }

            if (path.Skip(1).Any(string.IsNullOrEmpty))
            {
                return $"row {row.Key} has an empty table key in its path";
            }
        }

        return null;
    }
}

public sealed record SavedVariableOwner(string Folder, string Title, bool PerCharacter);

public sealed record SettingsSchemaPage(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("sections")] IReadOnlyList<SettingsSchemaSection>? Sections);

public sealed record SettingsSchemaSection(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("column")] int Column,
    [property: JsonPropertyName("rows")] IReadOnlyList<SettingsRow>? Rows);

public sealed record SettingsOption(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] JsonElement? Value,
    [property: JsonPropertyName("bit")] int? Bit);

public sealed record SettingsCondition(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("equals")] JsonElement Value);

public sealed record SettingsRule(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("min")] double? Min,
    [property: JsonPropertyName("max")] double? Max,
    [property: JsonPropertyName("empty")] bool? Empty,
    [property: JsonPropertyName("lower")] bool Lower);

public sealed record SettingsRow(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("tooltip")] string? Tooltip,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("placeholder")] string? Placeholder,
    [property: JsonPropertyName("maxLetters")] int? MaxLetters,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("path")] IReadOnlyList<string>? Path,
    [property: JsonPropertyName("default")] JsonElement? Default,
    [property: JsonPropertyName("defaultByClass")] IReadOnlyDictionary<string, JsonElement>? DefaultByClass,
    [property: JsonPropertyName("options")] IReadOnlyList<SettingsOption>? Options,
    [property: JsonPropertyName("parent")] string? Parent,
    [property: JsonPropertyName("lockedWhenLoaded")] IReadOnlyList<string>? LockedWhenLoaded,
    [property: JsonPropertyName("visibleWhen")] SettingsCondition? VisibleWhen,
    [property: JsonPropertyName("rule")] SettingsRule? Rule,
    [property: JsonPropertyName("keepDefault")] bool KeepDefault,
    [property: JsonPropertyName("optionsByClass")] IReadOnlyDictionary<string, IReadOnlyList<SettingsOption>>? OptionsByClass = null,
    [property: JsonPropertyName("dynamicOptions")] bool DynamicOptions = false,
    [property: JsonPropertyName("visibleWhenLoaded")] IReadOnlyList<string>? VisibleWhenLoaded = null,
    [property: JsonPropertyName("hiddenWhenLoaded")] IReadOnlyList<string>? HiddenWhenLoaded = null)
{
    private static readonly HashSet<string> KnownTypes = new(["checkbox", "dropdown", "bitmask", "text", "money", "mailTarget", "note"], StringComparer.Ordinal);

    private static readonly HashSet<string> KnownScopes = new(["account", "character", "characterFile", "class"], StringComparer.Ordinal);

    public bool IsKnownType => KnownTypes.Contains(Type) && (Scope is null || KnownScopes.Contains(Scope));

    public bool IsCharacter => Scope is "character" or "characterFile" or "class";

    public bool IsCharacterFile => Scope == "characterFile";

    public bool IsClass => Scope == "class";

    public bool IsSetting => Type != "note" && Key is not null && Path is { Count: > 0 };

    public IReadOnlyList<SettingsOption> OptionsFor(string? classToken) =>
        classToken is not null && OptionsByClass?.GetValueOrDefault(classToken) is { } byClass ? byClass : Options ?? [];
}

public sealed record SettingChange(SettingsRow Row, string? Character, LuaValue Value);

public static partial class AddonSettings
{
    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z")]
    internal static partial Regex GlobalName();

    // A global two folders declare has no single owner, so its rows stay read-only.
    public static IReadOnlyDictionary<string, SavedVariableOwner?> SavedVariableOwners(string addOnsPath, IEnumerable<string> folders, int? clientInterface = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var owners = new Dictionary<string, SavedVariableOwner?>(StringComparer.Ordinal);
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(addOnsPath, folder);
            if (!Directory.Exists(path) || LocalAddons.TopLevelToc(path, folder, clientInterface) is not { } toc)
            {
                continue;
            }

            var title = LocalAddons.Read(addOnsPath, folder, clientInterface)?.Name ?? folder;
            foreach (var (directive, perCharacter) in new[] { ("SavedVariables", false), ("SavedVariablesPerCharacter", true) })
            {
                foreach (var global in (TocFile.ReadDirective(toc, directive) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    owners[global] = owners.ContainsKey(global) ? null : new SavedVariableOwner(folder, title, perCharacter);
                }
            }
        }

        return owners;
    }

    public static SavedVariableOwner? OwnerOf(IReadOnlyDictionary<string, SavedVariableOwner?> owners, SettingsRow row)
    {
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(row);
        return row.IsSetting && owners.GetValueOrDefault(row.Path![0]) is { } owner && owner.PerCharacter == row.IsCharacterFile ? owner : null;
    }

    public static string NotLoggedInNotice(string title) => $"Log in once with {title} enabled before changing these settings";

    public static IEnumerable<SettingChange> AbsentKeptDefaults(IReadOnlyDictionary<string, LuaValue> globals, IEnumerable<SettingChange> candidates, Func<string?, string?> classOf) =>
        candidates.Where(change => change.Row.KeepDefault
            && (!change.Row.IsCharacter || change.Character is not null)
            && (!change.Row.IsClass || classOf(change.Character) is not null)
            && Stored(globals, change.Row, change.Character, classOf(change.Character)) is null);

    public const string CharacterToken = "{character}";

    public const string ClassToken = "{class}";

    public const string NeverMailTarget = "-";

    public const string RunningMessage = "World of Warcraft is running. Close the game to change these settings.";

    public static string FilePath(string flavourPath, string account, string folder) =>
        Path.Combine(flavourPath, "WTF", "Account", account, "SavedVariables", folder + ".lua");

    public static string AccountFolder(string flavourPath, string account) => Path.Combine(flavourPath, "WTF", "Account", account);

    // Latin1 maps every byte to one char, so strings the client wrote as raw bytes are written back byte for byte.
    public static IReadOnlyDictionary<string, LuaValue> Load(string file) =>
        File.Exists(file) ? LuaSavedVariables.Parse(Encoding.Latin1.GetString(File.ReadAllBytes(file))) : new Dictionary<string, LuaValue>(StringComparer.Ordinal);

    public static void Write(string file, IReadOnlyDictionary<string, LuaValue> globals, Func<bool> clientRunning)
    {
        ArgumentNullException.ThrowIfNull(clientRunning);
        if (clientRunning())
        {
            throw new InvalidOperationException(RunningMessage);
        }

        // A file the game has not written yet is left for the game to create, so the addon's own first-run setup still runs.
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} does not exist yet. Log in once with that addon enabled before changing these settings.");
        }

        var temp = file + ".steward-settings";
        File.WriteAllBytes(temp, Encoding.Latin1.GetBytes(LuaWriter.SerializeGlobals(globals)));
        if (clientRunning())
        {
            File.Delete(temp);
            throw new InvalidOperationException(RunningMessage);
        }

        File.Move(temp, file, overwrite: true);
    }

    public static string ToFile(string text) => Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(text));

    public static string FromFile(string text) => Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(text));

    public static LuaValue ToLua(JsonElement? element) => element?.ValueKind switch
    {
        JsonValueKind.True => LuaValue.FromBoolean(true),
        JsonValueKind.False => LuaValue.FromBoolean(false),
        JsonValueKind.Number => LuaValue.FromNumber(element.Value.GetDouble()),
        JsonValueKind.String => LuaValue.FromString(element.Value.GetString()!),
        _ => LuaValue.Nil,
    };

    public static bool Same(LuaValue a, LuaValue b) =>
        a.Kind == b.Kind && a.Kind switch
        {
            LuaKind.Text => string.Equals(a.Text, b.Text, StringComparison.Ordinal),
            LuaKind.Number => a.Number.Equals(b.Number),
            LuaKind.Boolean => a.Boolean == b.Boolean,
            LuaKind.Nil => true,
            _ => false,
        };

    public static IReadOnlyList<string> Characters(IReadOnlyDictionary<string, LuaValue> globals, string global = "HoobiScriptsDB") =>
        globals.GetValueOrDefault(global)?.GetTable("chars") is { } chars
            ? [.. chars.Table.Where(entry => entry.Key is { Kind: LuaKind.Text } && entry.Value.Kind == LuaKind.Table)
                .Select(entry => FromFile(entry.Key!.Text!))
                .Order(StringComparer.OrdinalIgnoreCase)]
            : [];

    // The live client's file can carry entries the Forever copy wizard brought over from the beta realm.
    public static IReadOnlyList<string> CharactersOn(IReadOnlyList<string> characters, string? productCode) =>
        productCode == ForeverLaunch.LiveProduct
            ? [.. characters.Where(key => !(key.IndexOf('-', StringComparison.Ordinal) is var split and > 0
                && string.Equals(key[(split + 1)..], ForeverLaunch.BetaRealm, StringComparison.OrdinalIgnoreCase)))]
            : characters;

    public static string? ClassOf(IReadOnlyDictionary<string, LuaValue> globals, string? character, string global = "HoobiScriptsDB") =>
        character is null ? null : globals.GetValueOrDefault(global)?.GetTable("chars")?.GetTable(ToFile(character))?.GetString("class");

    public static string CharacterLabel(string character) =>
        character.IndexOf('-', StringComparison.Ordinal) is var split and > 0 ? $"{character[..split]} ({character[(split + 1)..]})" : character;

    public static LuaValue? Stored(IReadOnlyDictionary<string, LuaValue> globals, SettingsRow row, string? character, string? classToken = null)
    {
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(row);
        if (TablePath(row, character) is not { } path || KeyFor(row, classToken) is not { } key || globals.GetValueOrDefault(path[0]) is not { } table)
        {
            return null;
        }

        foreach (var part in path.Skip(1))
        {
            if (table.GetTable(part) is not { } next)
            {
                return null;
            }

            table = next;
        }

        return table.Get(key) is { Kind: not LuaKind.Nil } value ? Decode(value) : null;
    }

    public static LuaValue Default(SettingsRow row, string? classToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.DefaultByClass is { } byClass && classToken is not null && byClass.TryGetValue(classToken, out var seeded)
            ? ToLua(seeded)
            : ToLua(row.Default);
    }

    public static LuaValue Resolve(IReadOnlyDictionary<string, LuaValue> globals, SettingsRow row, string? character, string? classToken) =>
        Stored(globals, row, character, classToken) ?? Default(row, classToken);

    public static IReadOnlyDictionary<string, LuaValue> Apply(
        IReadOnlyDictionary<string, LuaValue> globals, IEnumerable<SettingChange> changes, Func<string?, string?> classOf)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(classOf);
        var result = new Dictionary<string, LuaValue>(globals, StringComparer.Ordinal);
        foreach (var change in changes)
        {
            var classToken = classOf(change.Character);
            var path = TablePath(change.Row, change.Character) ?? throw new InvalidOperationException($"{change.Row.Key} has no path");
            var key = KeyFor(change.Row, classToken) ?? throw new InvalidOperationException($"{change.Row.Key} needs the character's class");
            var remove = !change.Row.KeepDefault && IsRemovableDefault(change.Row, change.Value, classToken);
            if (remove && Stored(result, change.Row, change.Character, classToken) is null)
            {
                continue;
            }

            result[path[0]] = SetIn(result.GetValueOrDefault(path[0]), path, 1, key, remove ? null : Encode(change.Value));
        }

        return result;
    }

    public static readonly IReadOnlyList<string> ClassTokens = ["WARRIOR", "PALADIN", "HUNTER", "ROGUE", "PRIEST", "SHAMAN", "MAGE", "WARLOCK", "DRUID"];

    public static string ClassLabel(string classToken) =>
        classToken.Length == 0 ? classToken : classToken[0] + classToken[1..].ToLower(CultureInfo.InvariantCulture);

    public static IReadOnlyDictionary<string, LuaValue> AddCharacter(
        IReadOnlyDictionary<string, LuaValue> globals, string character, string? classToken, IEnumerable<SettingsRow> seededRows, string global = "HoobiScriptsDB")
    {
        ArgumentNullException.ThrowIfNull(globals);
        var result = new Dictionary<string, LuaValue>(globals, StringComparer.Ordinal);
        if (result.GetValueOrDefault(global)?.GetTable("chars")?.GetTable(ToFile(character)) is null)
        {
            result[global] = SetIn(result.GetValueOrDefault(global), [global, "chars"], 1, ToFile(character), LuaValue.FromTable());
        }

        if (classToken is not null)
        {
            result[global] = SetIn(result[global], [global, "chars", ToFile(character)], 1, "class", LuaValue.FromString(classToken));
            return result;
        }

        return Apply(result, seededRows.Where(row => row.Scope == "character" && row.DefaultByClass is not null).Select(row => new SettingChange(row, character, Default(row, null))), _ => null);
    }

    public static bool AnyInstalled(string addOnsPath, IEnumerable<string>? folders)
    {
        if (folders is null || !Directory.Exists(addOnsPath))
        {
            return false;
        }

        var installed = Directory.EnumerateDirectories(addOnsPath).Select(Path.GetFileName).OfType<string>().ToList();
        return folders.Any(folder => folder.EndsWith('*')
            ? installed.Any(name => name.StartsWith(folder[..^1], StringComparison.OrdinalIgnoreCase))
            : installed.Any(name => string.Equals(name, folder, StringComparison.OrdinalIgnoreCase)));
    }

    public static string? CharacterFolder(string accountFolder, string character, IReadOnlyDictionary<string, string> knownRealms)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(knownRealms);
        if (character.IndexOf('-', StringComparison.Ordinal) is not (> 0 and var split) || !knownRealms.TryGetValue(character[(split + 1)..], out var realmId))
        {
            return null;
        }

        var realmFolder = Path.Combine(accountFolder, realmId);
        var name = character[..split].Replace(' ', '-');
        return Directory.Exists(realmFolder)
            ? Directory.EnumerateDirectories(realmFolder).FirstOrDefault(folder => string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public static string CharacterFilePath(string characterFolder, string folder) => Path.Combine(characterFolder, "SavedVariables", folder + ".lua");

    // With no class known a class-seeded key is always written, since the addon's own seeding would otherwise pick for that character.
    private static bool IsRemovableDefault(SettingsRow row, LuaValue value, string? classToken) =>
        (row.DefaultByClass is null || (classToken is not null && row.DefaultByClass.ContainsKey(classToken)))
        && Same(value, Default(row, classToken));

    private static List<string>? TablePath(SettingsRow row, string? character)
    {
        if (!row.IsSetting || (row.IsCharacter && character is null))
        {
            return null;
        }

        return [.. row.Path!.Select(part => part == CharacterToken ? ToFile(character!) : ToFile(part))];
    }

    private static string? KeyFor(SettingsRow row, string? classToken) =>
        row.Key == ClassToken ? classToken : row.Key is { } key ? ToFile(key) : null;

    private static LuaValue SetIn(LuaValue? table, IReadOnlyList<string> path, int index, string key, LuaValue? value)
    {
        if (table is not null && table.Kind != LuaKind.Table)
        {
            throw new InvalidOperationException($"{string.Join(".", path.Take(index))} is not a table");
        }

        var entries = table?.Table.ToList() ?? [];
        var name = index < path.Count ? path[index] : key;
        var at = entries.FindIndex(entry => entry.Key is { Kind: LuaKind.Text } k && string.Equals(k.Text, name, StringComparison.Ordinal));
        if (index < path.Count)
        {
            var child = SetIn(at >= 0 ? entries[at].Value : null, path, index + 1, key, value);
            if (at >= 0)
            {
                entries[at] = entries[at] with { Value = child };
            }
            else
            {
                entries.Add(new LuaEntry(LuaValue.FromString(name), child));
            }
        }
        else if (value is null)
        {
            if (at >= 0)
            {
                entries.RemoveAt(at);
            }
        }
        else if (at >= 0)
        {
            entries[at] = entries[at] with { Value = value };
        }
        else
        {
            entries.Add(new LuaEntry(LuaValue.FromString(name), value));
        }

        return LuaValue.FromTable(entries);
    }

    private static LuaValue Decode(LuaValue value) => value.Kind == LuaKind.Text ? LuaValue.FromString(FromFile(value.Text!)) : value;

    private static LuaValue Encode(LuaValue value) => value.Kind == LuaKind.Text ? LuaValue.FromString(ToFile(value.Text!)) : value;

    public static string? Check(SettingsRow row, string input, out LuaValue? value)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(input);
        var rule = row.Rule;
        var text = input.Trim();
        value = LuaValue.FromString(text);
        switch (rule?.Kind)
        {
            case "integer":
                if (text.Length == 0)
                {
                    return rule.Empty == true ? null : "Enter a number";
                }

                if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                    || (rule.Min is { } min && number < min)
                    || (rule.Max is { } max && number > max))
                {
                    value = null;
                    return rule.Min is { } low && rule.Max is { } high
                        ? string.Create(CultureInfo.InvariantCulture, $"Enter a whole number from {low} to {high}")
                        : "Enter a whole number";
                }

                value = ToLua(row.Default).Kind == LuaKind.Number ? LuaValue.FromNumber(number) : LuaValue.FromString(number.ToString(CultureInfo.InvariantCulture));
                return null;
            case "characterName":
                if (text.Any(c => !char.IsLetter(c) && c is not (' ' or '\'' or '-')))
                {
                    value = null;
                    return "Enter a character name";
                }

                return null;
            case "string":
                if (text.Length == 0 && rule.Empty == false)
                {
                    value = null;
                    return "Enter a value";
                }

                value = LuaValue.FromString(rule.Lower ? text.ToLowerInvariant() : text);
                return null;
            default:
                value = LuaValue.FromString(input);
                return null;
        }
    }

    public static string FormatMoney(double copper)
    {
        var total = (long)Math.Max(0, copper);
        var parts = new List<string>();
        if (total >= 10000)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{total / 10000}g"));
        }

        if (total / 100 % 100 is var silver and > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{silver}s"));
        }

        if (total % 100 > 0 || parts.Count == 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{total % 100}c"));
        }

        return string.Join(" ", parts);
    }

    public const long MaxGold = 9_999_999;

    public static long? ParseMoney(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        long total = 0;
        var seen = new HashSet<char>();
        foreach (var part in parts)
        {
            var unit = char.ToLowerInvariant(part[^1]);
            var digits = char.IsAsciiDigit(unit) ? part : part[..^1];
            unit = char.IsAsciiDigit(unit) ? 'g' : unit;
            if (unit is not ('g' or 's' or 'c') || !seen.Add(unit)
                || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
                || amount > (unit == 'g' ? MaxGold : 99))
            {
                return null;
            }

            total = checked(total + (unit switch { 'g' => amount * 10000, 's' => amount * 100, _ => amount }));
        }

        return total;
    }

    public static IReadOnlyDictionary<string, string> KnownRealms(string accountFolder, IEnumerable<string> characters, string? connectionLog = null)
    {
        var realmsByName = characters
            .Select(key => key.IndexOf('-', StringComparison.Ordinal) is var split and > 0 ? (Name: key[..split], Realm: key[(split + 1)..]) : default)
            .Where(entry => entry.Name is not null)
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Realm).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var ids = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(accountFolder))
        {
            return new Dictionary<string, string>();
        }

        foreach (var realmFolder in Directory.EnumerateDirectories(accountFolder))
        {
            var id = Path.GetFileName(realmFolder);
            if (string.Equals(id, "SavedVariables", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var characterFolder in Directory.EnumerateDirectories(realmFolder))
            {
                if (realmsByName.GetValueOrDefault(Path.GetFileName(characterFolder).Replace('-', ' ')) is not [var realm])
                {
                    continue;
                }

                ids[realm] = ids.TryGetValue(realm, out var known) && !string.Equals(known, id, StringComparison.OrdinalIgnoreCase) ? null : id;
            }
        }

        var named = ids.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, id) in LoggedRealms(connectionLog))
        {
            if (!ids.ContainsKey(name) && !named.Contains(id) && Directory.Exists(Path.Combine(accountFolder, id)))
            {
                ids[name] = id;
            }
        }

        return ids.Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> LoggedRealms(string? connectionLog)
    {
        const string Marker = "realmEntry=\"";
        var realms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (connectionLog is null || !File.Exists(connectionLog))
        {
            return realms;
        }

        try
        {
            using var reader = new StreamReader(new FileStream(connectionLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            while (reader.ReadLine() is { } line)
            {
                var start = line.IndexOf(Marker, StringComparison.Ordinal);
                var end = line.LastIndexOf('}');
                if (start < 0 || end < start)
                {
                    continue;
                }

                try
                {
                    using var entry = JsonDocument.Parse(line.AsMemory(start + Marker.Length, end - start - Marker.Length + 1));
                    if (entry.RootElement.TryGetProperty("wowRealmAddress", out var address) && address.TryGetUInt32(out var realmAddress)
                        && entry.RootElement.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } realmName)
                    {
                        realms[realmName] = (realmAddress >> 24).ToString(CultureInfo.InvariantCulture);
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return realms;
    }

    public static string? CheckNamePart(string part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var name = part.Trim();
        return name.Length == 0 ? "Enter a name"
            : name.Length > 12 ? "Use 12 letters or fewer"
            : name.All(c => char.IsLetter(c) || c is '\'') ? null
            : "Use letters and apostrophes only";
    }

    public static string NamePart(string part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var name = part.Trim();
        return name.Length == 0 ? name : char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];
    }

    public static string CharacterKey(string first, string last, string realm) => $"{NamePart(first.Trim())} {NamePart(last.Trim())}-{realm}";

    public static IReadOnlyList<(string First, string Last, string Realm)> UnrecordedCharacters(
        string accountFolder, IReadOnlyDictionary<string, string> knownRealms, IReadOnlyList<string> characters) =>
        [.. knownRealms.OrderBy(realm => realm.Key, StringComparer.OrdinalIgnoreCase)
            .Where(realm => Directory.Exists(Path.Combine(accountFolder, realm.Value)))
            .SelectMany(realm => Directory.EnumerateDirectories(Path.Combine(accountFolder, realm.Value))
                .Select(Path.GetFileName).OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(folder => folder.Split('-'))
                .Where(parts => parts is [var first, var last] && CheckNamePart(first) is null && CheckNamePart(last) is null)
                .Select(parts => (First: parts[0], Last: parts[1], Realm: realm.Key)))
            .Where(entry => !characters.Contains(CharacterKey(entry.First, entry.Last, entry.Realm), StringComparer.OrdinalIgnoreCase))];

    public static string CreateCharacterFolder(string accountFolder, string realmId, string first, string last)
    {
        var folder = Path.Combine(accountFolder, realmId, $"{NamePart(first.Trim())}-{NamePart(last.Trim())}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
