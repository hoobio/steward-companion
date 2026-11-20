using System.Globalization;
using System.Text.Json;

namespace Steward.Core;

public static class SyncDocument
{
    public const int Version = 1;

    private const string IntKeys = "$int";

    private const long MaxExactInteger = 1L << 53;

    public static SyncPayload Parse(string json, DateTimeOffset writtenAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the sync document is not a JSON object");
            }

            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != Version)
            {
                throw new FormatException($"the sync document version is not {Version}");
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the sync document payload is not an object");
            }

            var entries = ToLua(payload).Table;
            return new SyncPayload(writtenAt, entries)
            {
                SignOut = root.TryGetProperty("signOut", out var signOut) && signOut.ValueKind == JsonValueKind.String && signOut.GetString() == "dropMe"
                    ? SyncSignOut.DropMe
                    : SyncSignOut.Clear,
                Images = Images(root),
                Directory = ReadDirectory(payload),
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new FormatException($"the sync document is not valid: {ex.Message}", ex);
        }
    }

    public static LuaValue ToLua(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object when IntTable(element) is { } table => table,
        JsonValueKind.Object => LuaValue.FromTable([.. element.EnumerateObject()
            .Where(member => member.Value.ValueKind != JsonValueKind.Null)
            .Select(member => new LuaEntry(LuaValue.FromString(member.Name), ToLua(member.Value)))]),
        JsonValueKind.Array => LuaValue.Array(element.EnumerateArray().Select(ToLua)),
        JsonValueKind.String => LuaValue.FromString(element.GetString()!),
        JsonValueKind.Number when element.TryGetDouble(out var value) && double.IsFinite(value) => LuaValue.FromNumber(value),
        JsonValueKind.Number => throw new FormatException($"the number {element.GetRawText()} is out of range"),
        JsonValueKind.True => LuaValue.FromBoolean(true),
        JsonValueKind.False => LuaValue.FromBoolean(false),
        _ => LuaValue.Nil,
    };

    private static LuaValue? IntTable(JsonElement element)
    {
        using var members = element.EnumerateObject();
        if (!members.MoveNext() || members.Current.Name != IntKeys || members.Current.Value.ValueKind != JsonValueKind.Object || members.MoveNext())
        {
            return null;
        }

        var entries = new List<LuaEntry>();
        foreach (var member in element.GetProperty(IntKeys).EnumerateObject())
        {
            var digits = member.Name.StartsWith('-') ? member.Name[1..] : member.Name;
            if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)
                || !long.TryParse(member.Name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var key)
                || key is < -MaxExactInteger or > MaxExactInteger)
            {
                throw new FormatException($"the {IntKeys} key {member.Name} is not a base-10 integer");
            }

            if (member.Value.ValueKind != JsonValueKind.Null)
            {
                entries.Add(new LuaEntry(LuaValue.FromNumber(key), ToLua(member.Value)));
            }
        }

        return LuaValue.FromTable(entries);
    }

    private static SyncImages Images(JsonElement root)
    {
        if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Object)
        {
            return SyncImages.None;
        }

        return new SyncImages(
            images.TryGetProperty("guild", out var guild) && guild.ValueKind == JsonValueKind.String ? guild.GetString() : null,
            Urls(images, "people"),
            Urls(images, "discord"));
    }

    private static Dictionary<string, string> Urls(JsonElement images, string name)
    {
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        if (images.TryGetProperty(name, out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var member in map.EnumerateObject().Where(member => member.Value.ValueKind == JsonValueKind.String))
            {
                urls[member.Name] = member.Value.GetString()!;
            }
        }

        return urls;
    }

    private static SyncDirectory ReadDirectory(JsonElement payload)
    {
        var directory = Property(payload, "directory", JsonValueKind.Object) ?? default;
        var people = Property(directory, "people", JsonValueKind.Array);
        var characters = Property(directory, "characters", JsonValueKind.Array);
        var professions = Property(directory, "professions", JsonValueKind.Array);
        return new SyncDirectory(
            people is { } peopleList
                ? [.. Tables(peopleList).Where(person => Text(person, "id") is not null)
                    .Select(person => new DirectoryPerson(Text(person, "id")!, Text(person, "name") ?? "", Text(person, "mainGuid")))]
                : null,
            characters is { } characterList
                ? [.. Tables(characterList).Where(character => Text(character, "guid") is not null)
                    .Select(character => new DirectoryCharacter(Text(character, "guid")!, Text(character, "name") ?? "", Integer(character, "level"), Integer(character, "classId"), Text(character, "linkedUserId")))]
                : null,
            professions?.GetArrayLength(),
            Property(payload, "members", JsonValueKind.Array)?.GetArrayLength() ?? 0,
            Property(payload, "catalogue", JsonValueKind.Object)?.EnumerateObject().Count() ?? 0);
    }

    private static IEnumerable<JsonElement> Tables(JsonElement array) =>
        array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object);

    private static JsonElement? Property(JsonElement element, string name, JsonValueKind kind) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : null;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name, JsonValueKind.String)?.GetString();

    private static int Integer(JsonElement element, string name) =>
        Property(element, name, JsonValueKind.Number) is { } number && number.TryGetInt32(out var value) ? value : 0;
}
