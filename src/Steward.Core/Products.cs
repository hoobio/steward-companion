using System.Text.Json;
using System.Text.Json.Serialization;

namespace Steward.Core;

public sealed record ProductEvent(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("starts_at")] long StartsAt,
    [property: JsonPropertyName("ends_at")] long? EndsAt = null,
    [property: JsonPropertyName("headline")] bool Headline = false,
    [property: JsonPropertyName("upcoming_text")] string? UpcomingText = null,
    [property: JsonPropertyName("active_text")] string? ActiveText = null)
{
    public ScheduleEvent ToSchedule() => new(
        Name,
        DateTimeOffset.FromUnixTimeMilliseconds(StartsAt),
        EndsAt is { } end ? DateTimeOffset.FromUnixTimeMilliseconds(end) : null,
        Headline,
        UpcomingText,
        ActiveText);
}

public sealed record ProductOverride(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("icon_url")] string? IconUrl = null,
    [property: JsonPropertyName("events")] IReadOnlyList<ProductEvent>? Events = null);

public static class AppProducts
{
    public static IReadOnlyList<ProductOverride> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("products", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var products = new List<ProductOverride>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object || String(element, "code") is not { Length: > 0 } code)
            {
                continue;
            }

            var iconUrl = String(element, "icon_url");
            products.Add(new(
                code,
                String(element, "name") is { Length: > 0 } name ? name : null,
                Uri.TryCreate(iconUrl, UriKind.Absolute, out var icon) && icon.Scheme == Uri.UriSchemeHttps ? iconUrl : null,
                element.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array
                    ? [.. events.EnumerateArray().Select(Event).OfType<ProductEvent>()]
                    : null));
        }

        return products;
    }

    public static IReadOnlyDictionary<string, string> Names(IReadOnlyDictionary<string, string> bundled, IReadOnlyList<ProductOverride>? overrides) =>
        bundled.ToDictionary(
            product => product.Key,
            product => overrides?.FirstOrDefault(entry => entry.Code == product.Key)?.Name ?? product.Value);

    public static IReadOnlyList<ScheduleEvent> Schedule(string? productCode, IReadOnlyList<ProductOverride>? overrides) =>
        overrides?.FirstOrDefault(entry => entry.Code == productCode)?.Events is { } events ? [.. events.Select(entry => entry.ToSchedule())]
        : ForeverLaunch.IsForever(productCode) ? ForeverSchedule.Events
        : [];

    private static ProductEvent? Event(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || String(element, "name") is not { Length: > 0 } name
            || Millis(element, "starts_at") is not { } start)
        {
            return null;
        }

        var end = Millis(element, "ends_at");
        return end < start ? null : new(
            name,
            start,
            end,
            element.TryGetProperty("headline", out var headline) && headline.ValueKind == JsonValueKind.True,
            String(element, "upcoming_text"),
            String(element, "active_text"));
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Millis(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var millis)
            && millis is >= -62135596800000 and <= 253402300799999 ? millis : null;
}
