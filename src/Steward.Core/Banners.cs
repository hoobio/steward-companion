using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed class BannerIdConverter : JsonConverter<string>
{
    // gigagrug's id is documented as a string but has shipped as a JSON number (5425bf8); either form must parse rather than dropping the banner.
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString() ?? "",
        JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
        _ => throw new JsonException($"Unexpected token {reader.TokenType} for banner id"),
    };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

public sealed record BannerAction(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("page")] string? Page = null);

public sealed record Banner(
    [property: JsonPropertyName("id"), JsonConverter(typeof(BannerIdConverter))] string Id,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("level")] string? Level = null,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("dismissible")] bool Dismissible = true,
    [property: JsonPropertyName("min_version")] string? MinVersion = null,
    [property: JsonPropertyName("max_version")] string? MaxVersion = null,
    [property: JsonPropertyName("channels")] IReadOnlyList<string>? Channels = null,
    [property: JsonPropertyName("actions")] IReadOnlyList<BannerAction>? Actions = null);

public enum BannerLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public static class BannerLevels
{
    // An unknown or missing level renders as Informational, since banners are entirely server-defined and a future level must never break rendering.
    public static BannerLevel Parse(string? level) => level?.ToLowerInvariant() switch
    {
        "success" => BannerLevel.Success,
        "warning" => BannerLevel.Warning,
        "error" => BannerLevel.Error,
        _ => BannerLevel.Info,
    };
}

public enum BannerActionKind
{
    StoreUpdate,
    OpenUrl,
    Navigate,
    Dismiss,
    Unknown,
}

public static class BannerActions
{
    private static readonly string[] AllowedUrlSchemes = ["https", "ms-windows-store", "discord"];

    // An unknown type hides its button only; the rest of the banner still renders.
    public static BannerActionKind Parse(string? type) => type switch
    {
        "store_update" => BannerActionKind.StoreUpdate,
        "open_url" => BannerActionKind.OpenUrl,
        "navigate" => BannerActionKind.Navigate,
        "dismiss" => BannerActionKind.Dismiss,
        _ => BannerActionKind.Unknown,
    };

    // Re-checked client-side regardless of what gigagrug allowed, since the server is a public API and this URL is about to be handed to ShellExecute.
    public static bool IsAllowedUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && AllowedUrlSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
}

public static class BannerParsing
{
    public static IReadOnlyList<Banner> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("banners", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var banners = new List<Banner>();
        foreach (var element in array.EnumerateArray())
        {
            try
            {
                if (element.Deserialize(CompanionJsonContext.Default.Banner) is { } banner)
                {
                    banners.Add(banner);
                }
            }
            catch (JsonException)
            {
                // One malformed banner is skipped; it never drops the rest of the response.
            }
        }

        return banners;
    }
}

public static class BannerFilter
{
    public static IReadOnlyList<Banner> Active(
        IReadOnlyList<Banner> banners,
        string installedVersion,
        string channel,
        IReadOnlyDictionary<string, int> dismissedRevisions)
    {
        var current = ParseVersion(installedVersion);
        return [.. banners.Where(banner =>
            !IsDismissed(banner, dismissedRevisions)
            && InChannel(banner, channel)
            && InVersionRange(banner, current))];
    }

    private static bool IsDismissed(Banner banner, IReadOnlyDictionary<string, int> dismissedRevisions) =>
        dismissedRevisions.TryGetValue(banner.Id, out var revision) && revision == banner.Revision;

    private static bool InChannel(Banner banner, string channel) =>
        banner.Channels is null or { Count: 0 } || banner.Channels.Contains(channel, StringComparer.OrdinalIgnoreCase);

    private static bool InVersionRange(Banner banner, int[] current)
    {
        if (banner.MinVersion is { } min && Compare(current, ParseVersion(min)) < 0)
        {
            return false;
        }

        if (banner.MaxVersion is { } max && Compare(current, ParseVersion(max)) > 0)
        {
            return false;
        }

        return true;
    }

    private static int[] ParseVersion(string value)
    {
        var parts = value.Split('.');
        var result = new int[4];
        for (var i = 0; i < 4; i++)
        {
            result[i] = i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        }

        return result;
    }

    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < 4; i++)
        {
            var cmp = a[i].CompareTo(b[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return 0;
    }
}
