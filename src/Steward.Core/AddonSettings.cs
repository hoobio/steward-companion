using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
}

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
    [property: JsonPropertyName("visibleWhenLoaded")] IReadOnlyList<string>? VisibleWhenLoaded = null)
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

public static class AddonSettings
{
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

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
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
                if (input.Length == 0 && rule.Empty == false)
                {
                    value = null;
                    return "Enter a value";
                }

                value = LuaValue.FromString(rule.Lower ? input.ToLowerInvariant() : input);
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
                || (unit != 'g' && amount > 99))
            {
                return null;
            }

            total += unit switch { 'g' => amount * 10000, 's' => amount * 100, _ => amount };
        }

        return total;
    }

    public static IReadOnlyDictionary<string, string> KnownRealms(string accountFolder, IEnumerable<string> characters)
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

        return ids.Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.OrdinalIgnoreCase);
    }

    public static string? CheckNamePart(string part) =>
        part.Length is > 0 and <= 12 && part.All(char.IsLetter) ? null : "Use letters only";

    public static string NamePart(string part) =>
        part.Length == 0 ? part : char.ToUpper(part[0], CultureInfo.InvariantCulture) + part[1..].ToLower(CultureInfo.InvariantCulture);

    public static string CharacterKey(string first, string last, string realm) => $"{NamePart(first.Trim())} {NamePart(last.Trim())}-{realm}";

    public static string CreateCharacterFolder(string accountFolder, string realmId, string first, string last)
    {
        var folder = Path.Combine(accountFolder, realmId, $"{NamePart(first.Trim())}-{NamePart(last.Trim())}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
