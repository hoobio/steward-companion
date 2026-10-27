using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Steward.Core;

public enum SettingsImportStatus
{
    Ready,
    Conflict,
    Refused,
}

public sealed class SettingsImportItem
{
    public const string ChangedInGame = "Changed in game since the export";

    public required string Addon { get; init; }

    public required string Page { get; init; }

    public required string Label { get; init; }

    public string? Old { get; init; }

    public string? New { get; init; }

    public SettingsImportStatus Status { get; init; }

    public string? Reason { get; init; }

    internal string? File { get; init; }

    internal string? Character { get; init; }

    internal Func<IReadOnlyDictionary<string, LuaValue>, IReadOnlyDictionary<string, LuaValue>>? Edit { get; init; }

    internal Action? Prepare { get; init; }

    internal SettingsImportItem Refuse(string reason) => new()
    {
        Addon = Addon,
        Page = Page,
        Label = Label,
        Old = Old,
        New = New,
        Status = SettingsImportStatus.Refused,
        Reason = reason,
    };
}

public sealed record SettingsImportPayload(string Product, string Account, JsonElement Root);

public sealed record SettingsImport(string Product, string Account, IReadOnlyList<SettingsImportItem> Items);

public static class AddonSettingsImport
{
    public static SettingsImportPayload Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object
                && Prop(root, "v") is { ValueKind: JsonValueKind.Number } version && version.TryGetInt32(out var number) && number == 1
                && Str(root, "product") is { Length: > 0 } product
                && Str(root, "account") is { Length: > 0 } account
                    ? new SettingsImportPayload(product, account, root)
                    : throw new FormatException(AddonSettingsPayload.NotALink);
        }
        catch (JsonException ex)
        {
            throw new FormatException(AddonSettingsPayload.NotALink, ex);
        }
    }

    public static SettingsImport Preview(SettingsImportPayload payload, SettingsAccount account, IReadOnlyList<SettingsSource> addons)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(addons);
        var newCharacters = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = new List<SettingsImportItem>();
        items.AddRange(Elements(payload.Root, "newCharacters").Select(entry => NewCharacter(entry, account, newCharacters)));
        items.AddRange(Elements(payload.Root, "changes").Select(change => Change(change, account, addons, newCharacters)));
        var plans = Prop(payload.Root, "plans");
        items.AddRange(Elements(plans, "talents").Select(plan => TalentPlan(plan, account)));
        items.AddRange(Elements(plans, "actionBars").Select(plan => ActionBarPlan(plan, account)));
        return new SettingsImport(payload.Product, payload.Account, items);
    }

    public static int Apply(SettingsImport import, SettingsAccount account, IReadOnlyList<SettingsSource> addons, IEnumerable<SettingsImportItem> chosen, Func<bool> clientRunning)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(clientRunning);
        var picked = chosen.ToHashSet();
        var items = import.Items.Where(item => picked.Contains(item) && item.Status != SettingsImportStatus.Refused && item.Edit is not null && item.File is not null).ToList();
        if (clientRunning())
        {
            throw new InvalidOperationException(AddonSettings.RunningMessage);
        }

        foreach (var item in items)
        {
            item.Prepare?.Invoke();
        }

        foreach (var group in items.GroupBy(item => item.File!, StringComparer.OrdinalIgnoreCase))
        {
            var globals = group.Aggregate(AddonSettings.Load(group.Key), (current, item) => item.Edit!(current));
            string? ClassOf(string? character) => AddonSettings.ClassOf(globals, character, SettingsAccount.CoreGlobal) ?? account.ClassOf(character);
            var characters = group.Select(item => item.Character).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            var kept = KeptRows(account, addons, group.Key, characters, ClassOf);
            globals = AddonSettings.Apply(globals, AddonSettings.AbsentKeptDefaults(globals, kept, ClassOf).ToList(), ClassOf);
            AddonSettings.Write(group.Key, globals, clientRunning);
        }

        return items.Count;
    }

    private static IEnumerable<SettingChange> KeptRows(
        SettingsAccount account, IReadOnlyList<SettingsSource> addons, string file, IReadOnlyList<string> characters, Func<string?, string?> classOf)
    {
        foreach (var row in Rows(addons).Select(entry => entry.Row).Where(row => row.KeepDefault && row.IsSetting && !row.IsClass && !row.IsCharacterFile))
        {
            foreach (var character in row.IsCharacter ? characters.Cast<string?>() : [null])
            {
                if (string.Equals(account.FileFor(row, character).File, file, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new SettingChange(row, character, AddonSettings.Default(row, classOf(character)));
                }
            }
        }
    }

    private static IEnumerable<(SettingsSchema Schema, SettingsSchemaPage Page, SettingsRow Row)> Rows(IReadOnlyList<SettingsSource> addons) =>
        addons.Where(source => source.Schema is not null)
            .SelectMany(source => (source.Schema!.Pages ?? []).SelectMany(page => (page.Sections ?? []).SelectMany(section => section.Rows ?? [])
                .Select(row => (source.Schema!, page, row))));

    private static SettingsImportItem NewCharacter(JsonElement entry, SettingsAccount account, Dictionary<string, string> newCharacters)
    {
        var first = Str(entry, "first")?.Trim() ?? "";
        var last = Str(entry, "last")?.Trim() ?? "";
        var classToken = Str(entry, "class");
        var realm = Str(entry, "realm") ?? "";
        var item = new SettingsImportItem
        {
            Addon = account.TitleOf(SettingsAccount.CoreGlobal),
            Page = "New character",
            Label = $"{AddonSettings.NamePart(first)} {AddonSettings.NamePart(last)} ({realm})",
            New = classToken is null ? null : AddonSettings.ClassLabel(classToken),
        };
        if (AddonSettings.CheckNamePart(first) is { } firstError)
        {
            return item.Refuse($"First name: {firstError}");
        }

        if (AddonSettings.CheckNamePart(last) is { } lastError)
        {
            return item.Refuse($"Last name: {lastError}");
        }

        if (classToken is null || !AddonSettings.ClassTokens.Contains(classToken))
        {
            return item.Refuse("Not a class");
        }

        if (account.KnownRealms.Keys.FirstOrDefault(known => string.Equals(known, realm, StringComparison.OrdinalIgnoreCase)) is not { } realmName)
        {
            return item.Refuse("Log in to a character on this realm once first");
        }

        var key = AddonSettings.CharacterKey(first, last, realmName);
        if (account.Characters.Concat(newCharacters.Keys).Any(character => string.Equals(character, key, StringComparison.OrdinalIgnoreCase)))
        {
            return item.Refuse("This character already has settings");
        }

        if (account.CoreFile is not { } file || !File.Exists(file))
        {
            return item.Refuse(account.CoreFile is null ? SettingsAccount.NoOwnerNotice : AddonSettings.NotLoggedInNotice(account.TitleOf(SettingsAccount.CoreGlobal)));
        }

        newCharacters[key] = classToken;
        var realmId = account.KnownRealms[realmName];
        return new SettingsImportItem
        {
            Addon = item.Addon,
            Page = item.Page,
            Label = AddonSettings.CharacterLabel(key),
            New = item.New,
            File = file,
            Character = key,
            Edit = globals => AddonSettings.AddCharacter(globals, key, classToken, [], SettingsAccount.CoreGlobal),
            Prepare = () => AddonSettings.CreateCharacterFolder(account.AccountFolder, realmId, first, last),
        };
    }

    private static SettingsImportItem Change(JsonElement change, SettingsAccount account, IReadOnlyList<SettingsSource> addons, Dictionary<string, string> newCharacters)
    {
        var folder = Str(change, "addon");
        var scope = Str(change, "scope") ?? "account";
        var key = Str(change, "key");
        var character = Str(change, "character");
        var to = Prop(change, "to");
        var refused = new SettingsImportItem { Addon = folder ?? "Unknown addon", Page = "", Label = key ?? "Unknown setting", New = Raw(to) };
        if (folder is null || key is null)
        {
            return refused.Refuse("Not a valid change");
        }

        var source = addons.FirstOrDefault(source => source.Schema is not null && string.Equals(source.Schema.Folder, folder, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return refused.Refuse("Steward has no settings for this addon on this install");
        }

        var match = Rows([source]).FirstOrDefault(entry => entry.Row.IsSetting && (entry.Row.Scope ?? "account") == scope && entry.Row.Key == key);
        if (match.Row is not { } row)
        {
            return refused.Refuse("Not a setting in the installed version");
        }

        var title = account.Owners.Values.FirstOrDefault(owner => owner is not null && string.Equals(owner.Folder, source.Folder, StringComparison.OrdinalIgnoreCase))?.Title ?? source.Folder;
        refused = new SettingsImportItem { Addon = title, Page = match.Page.Title, Label = row.Label ?? key, New = Raw(to) };
        if (account.Refusal(match.Schema, row) is { } readOnly)
        {
            return refused.Refuse(readOnly);
        }

        string? classToken;
        var known = true;
        if (row.IsClass)
        {
            classToken = character is null ? Str(change, "class") : newCharacters.GetValueOrDefault(character) ?? account.ClassOf(character);
            if (classToken is null || !AddonSettings.ClassTokens.Contains(classToken))
            {
                return refused.Refuse(character is null ? "Names no character" : "Steward does not know this character's class");
            }

            character = classToken;
            refused = new SettingsImportItem { Addon = title, Page = refused.Page, Label = $"{refused.Label} ({AddonSettings.ClassLabel(classToken)})", New = refused.New };
        }
        else if (row.IsCharacter)
        {
            if (character is null)
            {
                return refused.Refuse("Names no character");
            }

            known = account.Characters.Contains(character, StringComparer.Ordinal) || newCharacters.ContainsKey(character);
            classToken = newCharacters.GetValueOrDefault(character) ?? account.ClassOf(character);
            refused = new SettingsImportItem { Addon = title, Page = refused.Page, Label = $"{refused.Label}, {AddonSettings.CharacterLabel(character)}", New = refused.New };
        }
        else
        {
            character = null;
            classToken = null;
        }

        var (file, notice) = account.FileFor(row, character, known);
        if (notice is not null || file is null || account.Read(file) is not { } globals)
        {
            return refused.Refuse(notice ?? SettingsAccount.NoOwnerNotice);
        }

        if (CheckValue(row, to, classToken, out var value) is { } invalid)
        {
            return refused.Refuse(invalid);
        }

        var current = AddonSettings.Stored(globals, row, character, classToken);
        var conflict = !AddonSettings.Same(current ?? LuaValue.Nil, AddonSettings.ToLua(Prop(change, "from")));
        var forCharacter = character;
        return new SettingsImportItem
        {
            Addon = title,
            Page = refused.Page,
            Label = refused.Label,
            Old = Display(row, current ?? AddonSettings.Default(row, classToken), classToken) + (current is null ? " (default)" : ""),
            New = Display(row, value, classToken) + (to.ValueKind == JsonValueKind.Null ? " (default)" : ""),
            Status = conflict ? SettingsImportStatus.Conflict : SettingsImportStatus.Ready,
            Reason = conflict ? SettingsImportItem.ChangedInGame : null,
            File = file,
            Character = row.IsClass ? null : character,
            Edit = globals => AddonSettings.Apply(globals, [new SettingChange(row, forCharacter, value)], other => other == forCharacter ? classToken : account.ClassOf(other)),
        };
    }

    private static SettingsImportItem TalentPlan(JsonElement plan, SettingsAccount account)
    {
        var classToken = Str(plan, "class");
        var group = Prop(plan, "group") is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var parsed) ? parsed : 0;
        var to = Prop(plan, "to");
        var ids = Ids(to);
        var item = new SettingsImportItem
        {
            Addon = account.TitleOf(SettingsAccount.CoreGlobal),
            Page = "Talent plans",
            Label = $"{(classToken is null ? "Unknown class" : AddonSettings.ClassLabel(classToken))}, talent group {group}",
            New = ids is null ? null : Points(ids.Count),
        };
        if (classToken is null || !AddonSettings.ClassTokens.Contains(classToken))
        {
            return item.Refuse("Not a class");
        }

        if (group is not (1 or 2))
        {
            return item.Refuse("The talent group is not 1 or 2");
        }

        if (ids is null)
        {
            return item.Refuse("Talent ids must be positive whole numbers");
        }

        if (ids.Count > SettingsPlans.MaxTalentPoints)
        {
            return item.Refuse($"More than {SettingsPlans.MaxTalentPoints} talent points");
        }

        var from = Prop(plan, "from");
        if (from.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) && Ids(from) is null)
        {
            return item.Refuse("Not a valid change");
        }

        if (PlanFile(account, SettingsAccount.CoreGlobal) is { } refusal)
        {
            return item.Refuse(refusal);
        }

        var current = SettingsPlans.GetIndex(SettingsPlans.TalentPlans(account)?.GetTable(AddonSettings.ToFile(classToken)), group) is { Kind: LuaKind.Table } table
            ? SettingsPlans.Talents(table)
            : null;
        var expected = from.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : Ids(from);
        var conflict = current is null ? expected is not null : expected is null || !current.SequenceEqual(expected);
        var keys = new[] { LuaValue.FromString("talentPlans"), LuaValue.FromString(AddonSettings.ToFile(classToken)), LuaValue.FromNumber(group) };
        var written = LuaValue.Array(ids.Select(id => LuaValue.FromNumber(id)));
        return new SettingsImportItem
        {
            Addon = item.Addon,
            Page = item.Page,
            Label = item.Label,
            Old = current is null ? "No plan" : Points(current.Count),
            New = item.New,
            Status = conflict ? SettingsImportStatus.Conflict : SettingsImportStatus.Ready,
            Reason = conflict ? SettingsImportItem.ChangedInGame : null,
            File = account.AccountFileOf(SettingsAccount.CoreGlobal),
            Edit = globals => With(globals, SettingsAccount.CoreGlobal, keys, written),
        };
    }

    private static SettingsImportItem ActionBarPlan(JsonElement plan, SettingsAccount account)
    {
        var target = Str(plan, "target");
        var to = Prop(plan, "to");
        var toPlan = to.ValueKind == JsonValueKind.Object ? JsonNode.Parse(to.GetRawText()) as JsonObject : null;
        var item = new SettingsImportItem
        {
            Addon = account.TitleOf(SettingsAccount.ActionBarsGlobal),
            Page = "Action bar plans",
            Label = target == SettingsPlans.AccountTarget ? "Every class" : target is null ? "Unknown class" : AddonSettings.ClassLabel(target),
            New = toPlan is null ? null : SettingsPlans.Summary(toPlan),
        };
        if (target != SettingsPlans.AccountTarget && (target is null || !AddonSettings.ClassTokens.Contains(target)))
        {
            return item.Refuse("Not a class");
        }

        if (toPlan is null)
        {
            return item.Refuse("The plan is not a table");
        }

        if (PlanRefusal(to) is { } invalid)
        {
            return item.Refuse(invalid);
        }

        var from = Prop(plan, "from");
        if (from.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object))
        {
            return item.Refuse("Not a valid change");
        }

        if (PlanFile(account, SettingsAccount.ActionBarsGlobal) is { } refusal)
        {
            return item.Refuse(refusal);
        }

        var keys = target == SettingsPlans.AccountTarget
            ? new[] { LuaValue.FromString(SettingsPlans.AccountTarget) }
            : [LuaValue.FromString("plans"), LuaValue.FromString(AddonSettings.ToFile(target!))];
        var db = SettingsPlans.ActionBarsDb(account);
        var current = target == SettingsPlans.AccountTarget ? db?.GetTable(SettingsPlans.AccountTarget) : db?.GetTable("plans")?.GetTable(AddonSettings.ToFile(target!));
        var expected = from.ValueKind == JsonValueKind.Object ? SettingsPlans.PlanToJson(SettingsPlans.PlanToLua((JsonObject)JsonNode.Parse(from.GetRawText())!)) : null;
        var conflict = SettingsPlans.Canonical(current is null ? null : SettingsPlans.PlanToJson(current)) != SettingsPlans.Canonical(expected);
        var written = WithGeneratedMacros(SettingsPlans.PlanToLua(toPlan), current);
        return new SettingsImportItem
        {
            Addon = item.Addon,
            Page = item.Page,
            Label = item.Label,
            Old = SettingsPlans.Summary(current),
            New = item.New,
            Status = conflict ? SettingsImportStatus.Conflict : SettingsImportStatus.Ready,
            Reason = conflict ? SettingsImportItem.ChangedInGame : null,
            File = account.AccountFileOf(SettingsAccount.ActionBarsGlobal),
            Edit = globals => With(globals, SettingsAccount.ActionBarsGlobal, keys, written),
        };
    }

    private static LuaValue WithGeneratedMacros(LuaValue plan, LuaValue? current)
    {
        var generated = current?.GetTable("macros")?.Table
            .Where(entry => entry.Key is { Kind: LuaKind.Text } key && SettingsPlans.GeneratedMacros.Contains(AddonSettings.FromFile(key.Text!)))
            .ToList() ?? [];
        return generated.Count == 0
            ? plan
            : generated.Aggregate(plan, (table, entry) => SettingsPlans.SetPath(table, [LuaValue.FromString("macros"), entry.Key!], entry.Value));
    }

    private static string? PlanFile(SettingsAccount account, string global) =>
        account.AccountFileOf(global) is not { } file ? SettingsAccount.NoOwnerNotice
        : !File.Exists(file) ? AddonSettings.NotLoggedInNotice(account.TitleOf(global))
        : account.UnreadableNotice(file);

    private static Dictionary<string, LuaValue> With(IReadOnlyDictionary<string, LuaValue> globals, string global, IReadOnlyList<LuaValue> keys, LuaValue value) =>
        new Dictionary<string, LuaValue>(globals, StringComparer.Ordinal)
        {
            [global] = SettingsPlans.SetPath(globals.GetValueOrDefault(global), keys, value),
        };

    private static string Points(int count) => $"{count} {(count == 1 ? "point" : "points")}";

    private static List<long>? Ids(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ids = new List<long>();
        foreach (var item in element.EnumerateArray())
        {
            if (PositiveInteger(item) is not { } id)
            {
                return null;
            }

            ids.Add(id);
        }

        return ids;
    }

    private static long? PositiveInteger(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value) && value > 0 && value <= int.MaxValue ? value : null;

    public static string? PlanRefusal(JsonElement plan)
    {
        if (plan.ValueKind != JsonValueKind.Object)
        {
            return "The plan is not a table";
        }

        var macros = Prop(plan, "macros");
        if (macros.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
        {
            return "The plan's macros are not a table";
        }

        var defined = macros.ValueKind == JsonValueKind.Object ? macros.EnumerateObject().Select(macro => macro.Name).ToHashSet(StringComparer.Ordinal) : [];
        foreach (var property in plan.EnumerateObject())
        {
            if (property.Name == "macros")
            {
                continue;
            }

            if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var slot) || slot < 1 || slot > SettingsPlans.MaxSlot)
            {
                return $"{property.Name} is not a slot from 1 to {SettingsPlans.MaxSlot}";
            }

            if (EntryRefusal(property.Value, defined, allowList: true) is { } entry)
            {
                return $"Slot {slot}: {entry}";
            }
        }

        foreach (var macro in macros.ValueKind == JsonValueKind.Object ? macros.EnumerateObject().ToList() : [])
        {
            if (MacroRefusal(macro.Name, macro.Value) is { } reason)
            {
                return $"Macro {macro.Name}: {reason}";
            }
        }

        return null;
    }

    private static string? EntryRefusal(JsonElement entry, HashSet<string> macros, bool allowList)
    {
        string[] kinds = ["spell", "macro", "item", "profession", "list"];
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return "not a table";
        }

        var names = entry.EnumerateObject().Select(property => property.Name).ToList();
        var kind = names.Where(kinds.Contains).ToList();
        if (kind.Count != 1)
        {
            return "needs exactly one of spell, macro, item, profession or list";
        }

        if (names.Except([kind[0], .. kind[0] == "spell" ? ["fixed"] : Array.Empty<string>()]).FirstOrDefault() is { } extra)
        {
            return $"unknown field {extra}";
        }

        var value = entry.GetProperty(kind[0]);
        switch (kind[0])
        {
            case "spell":
                return PositiveInteger(value) is null ? "the spell is not a spell id"
                    : entry.TryGetProperty("fixed", out var isFixed) && isFixed.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ? "fixed is not true or false"
                    : null;
            case "item":
                return PositiveInteger(value) is null ? "the item is not an item id" : null;
            case "profession":
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var profession) && profession is 1 or 2 or 3 or 5 ? null : "the profession is not 1, 2, 3 or 5";
            case "macro":
                return value.ValueKind != JsonValueKind.String ? "the macro is not a name"
                    : macros.Contains(value.GetString()!) || SettingsPlans.GeneratedMacros.Contains(value.GetString()!) ? null
                    : $"the macro {value.GetString()} is not defined";
            default:
                if (!allowList || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                {
                    return "the list is not a list of entries";
                }

                return value.EnumerateArray().Select(item => EntryRefusal(item, macros, allowList: false)).FirstOrDefault(reason => reason is not null);
        }
    }

    private static string? MacroRefusal(string name, JsonElement macro)
    {
        if (SettingsPlans.GeneratedMacros.Contains(name))
        {
            return "the action bars addon generates this macro, so a plan cannot define it";
        }

        if (macro.ValueKind != JsonValueKind.Object)
        {
            return "not a table";
        }

        if (macro.EnumerateObject().Select(property => property.Name).Except(["perChar", "icon", "variants"]).FirstOrDefault() is { } extra)
        {
            return $"unknown field {extra}";
        }

        if (Prop(macro, "perChar").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return "perChar is not true or false";
        }

        if (!IsIcon(Prop(macro, "icon")))
        {
            return "the icon is not an icon";
        }

        if (Prop(macro, "variants") is not { ValueKind: JsonValueKind.Array } variants || variants.GetArrayLength() == 0)
        {
            return "has no variants";
        }

        foreach (var variant in variants.EnumerateArray())
        {
            if (variant.ValueKind != JsonValueKind.Object)
            {
                return "a variant is not a table";
            }

            if (variant.EnumerateObject().Select(property => property.Name).Except(["level", "body", "icon", "spells"]).FirstOrDefault() is { } unknown)
            {
                return $"unknown variant field {unknown}";
            }

            if (PositiveInteger(Prop(variant, "level")) is null)
            {
                return "a variant's level is not a positive whole number";
            }

            if (Prop(variant, "body") is not { ValueKind: JsonValueKind.String } body || body.GetString()!.Length > 255)
            {
                return "a variant's body is missing or longer than 255 characters";
            }

            if (!IsIcon(Prop(variant, "icon")))
            {
                return "a variant's icon is not an icon";
            }

            if (Prop(variant, "spells") is { ValueKind: not JsonValueKind.Undefined } spells
                && (spells.ValueKind != JsonValueKind.Array || spells.EnumerateArray().Any(spell => spell.ValueKind != JsonValueKind.String)))
            {
                return "a variant's spells are not spell names";
            }
        }

        return null;
    }

    private static bool IsIcon(JsonElement icon) => icon.ValueKind is JsonValueKind.Undefined or JsonValueKind.String || PositiveInteger(icon) is not null;

    public static string? CheckValue(SettingsRow row, JsonElement to, string? classToken, out LuaValue value)
    {
        ArgumentNullException.ThrowIfNull(row);
        value = LuaValue.Nil;
        if (to.ValueKind == JsonValueKind.Undefined)
        {
            return "Names no new value";
        }

        if (to.ValueKind == JsonValueKind.Null)
        {
            value = AddonSettings.Default(row, classToken);
            return null;
        }

        switch (row.Type)
        {
            case "checkbox":
                if (to.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return "Must be on or off";
                }

                value = LuaValue.FromBoolean(to.GetBoolean());
                return null;
            case "dropdown":
                var choice = AddonSettings.ToLua(to);
                if (!row.OptionsFor(classToken).Any(option => AddonSettings.Same(AddonSettings.ToLua(option.Value), choice)))
                {
                    return "Not one of the choices";
                }

                value = choice;
                return null;
            case "bitmask":
                var mask = row.Options?.Where(option => option.Bit is >= 0 and < 31).Aggregate(0L, (all, option) => all | (1L << option.Bit!.Value)) ?? 0;
                if (to.ValueKind != JsonValueKind.Number || !to.TryGetInt64(out var bits) || bits < 0 || (bits & ~mask) != 0)
                {
                    return "Not a valid set of options";
                }

                value = LuaValue.FromNumber(bits);
                return null;
            case "money":
                if (to.ValueKind != JsonValueKind.Number || !to.TryGetInt64(out var copper) || copper < 0 || copper > AddonSettings.MaxGold * 10000)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"Must be from 0 to {AddonSettings.MaxGold:N0} gold");
                }

                value = LuaValue.FromNumber(copper);
                return null;
            case "mailTarget":
                if (to.ValueKind != JsonValueKind.String)
                {
                    return "Must be a character name";
                }

                var target = to.GetString()!.Trim();
                if (target is not ("" or AddonSettings.NeverMailTarget) && target.Any(c => !char.IsLetter(c) && c is not (' ' or '\'' or '-')))
                {
                    return "Enter a character name";
                }

                if (row.MaxLetters is { } letters && target.Length > letters)
                {
                    return $"Use {letters} characters or fewer";
                }

                value = LuaValue.FromString(target);
                return null;
            default:
                var input = to.ValueKind switch
                {
                    JsonValueKind.String => to.GetString(),
                    JsonValueKind.Number => to.GetRawText(),
                    _ => null,
                };
                if (input is null)
                {
                    return "Must be text";
                }

                if (row.MaxLetters is { } maxLetters && input.Trim().Length > maxLetters)
                {
                    return $"Use {maxLetters} characters or fewer";
                }

                if (AddonSettings.Check(row, input, out var checkedValue) is { } error)
                {
                    return error;
                }

                value = checkedValue!;
                return null;
        }
    }

    public static string Display(SettingsRow row, LuaValue value, string? classToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(value);
        if (row.OptionsFor(classToken).FirstOrDefault(option => option.Value is not null && AddonSettings.Same(AddonSettings.ToLua(option.Value), value)) is { } match)
        {
            return match.Label;
        }

        return (row.Type, value.Kind) switch
        {
            ("bitmask", LuaKind.Number) => string.Join(", ", (row.Options ?? []).Where(option => option.Bit is { } bit && ((long)value.Number & (1L << bit)) != 0).Select(option => option.Label)) is { Length: > 0 } labels
                ? labels
                : "None",
            ("money", LuaKind.Number) => AddonSettings.FormatMoney(value.Number),
            ("mailTarget", LuaKind.Text) when value.Text == AddonSettings.NeverMailTarget => "Never",
            ("mailTarget", LuaKind.Text) when value.Text!.Length == 0 => "Same as the main setting",
            (_, LuaKind.Boolean) => value.Boolean ? "On" : "Off",
            (_, LuaKind.Number) => value.Number.ToString(CultureInfo.InvariantCulture),
            (_, LuaKind.Text) when value.Text!.Length > 0 => value.Text,
            _ => "Not set",
        };
    }

    private static string? Raw(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined => null,
        JsonValueKind.Null => "Default",
        JsonValueKind.True => "On",
        JsonValueKind.False => "Off",
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText(),
    };

    private static JsonElement Prop(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? Str(JsonElement element, string name) => Prop(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static List<JsonElement> Elements(JsonElement element, string name) =>
        Prop(element, name) is { ValueKind: JsonValueKind.Array } array ? [.. array.EnumerateArray()] : [];
}
