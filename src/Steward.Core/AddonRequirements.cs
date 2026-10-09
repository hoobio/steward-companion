using System.Text.Json;
using System.Text.Json.Serialization;

namespace Steward.Core;

[JsonConverter(typeof(AddonRequirementConverter))]
public sealed record AddonRequirement(
    string Id,
    string? Name = null,
    string? FolderName = null,
    string? ManifestBaseUrl = null,
    IReadOnlyList<AddonRequirement>? Requires = null);

public sealed class AddonRequirementConverter : JsonConverter<AddonRequirement>
{
    public override AddonRequirement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return From(document.RootElement);
    }

    private static AddonRequirement? From(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() is { Length: > 0 } id ? new AddonRequirement(id) : null;
        }

        if (element.ValueKind != JsonValueKind.Object || Text(element, "id") is not { Length: > 0 } objectId)
        {
            return null;
        }

        IReadOnlyList<AddonRequirement>? requires = element.TryGetProperty("requires", out var nested) && nested.ValueKind == JsonValueKind.Array
            ? [.. nested.EnumerateArray().Select(From).OfType<AddonRequirement>()]
            : null;
        return new AddonRequirement(objectId, Text(element, "name"), Text(element, "folder_name"), Text(element, "manifest_base_url"), requires);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public override void Write(Utf8JsonWriter writer, AddonRequirement value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value is { Name: null, FolderName: null, ManifestBaseUrl: null, Requires: null })
        {
            writer.WriteStringValue(value.Id);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("folder_name", value.FolderName);
        writer.WriteString("manifest_base_url", value.ManifestBaseUrl);
        if (value.Requires is { } requires)
        {
            writer.WritePropertyName("requires");
            writer.WriteStartArray();
            foreach (var requirement in requires)
            {
                Write(writer, requirement, options);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}

public sealed class AddonRequirementException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);

public sealed record AddonRequirementHooks(
    Func<AddonRequirement, ManagedAddon?> Resolve,
    Func<ManagedAddon, AddonRelease?, bool> IsSatisfied,
    Func<ManagedAddon, CancellationToken, Task<(string Channel, AddonRelease Release)?>> Fetch,
    Func<ManagedAddon, string, AddonRelease, CancellationToken, Task> Install,
    Action<string>? Status = null);

public static class AddonRequirements
{
    public const int MaxDepth = 3;

    private const string CurseForgePrefix = "curseforge:";

    public static int? CurseForgeModId(string requirementId) =>
        requirementId?.StartsWith(CurseForgePrefix, StringComparison.OrdinalIgnoreCase) == true
        && int.TryParse(requirementId.AsSpan(CurseForgePrefix.Length), out var modId) && modId > 0
            ? modId
            : null;

    public static bool Matches(string addonId, string requirementId) =>
        string.Equals(addonId, requirementId, StringComparison.OrdinalIgnoreCase)
        || (CurseForgeModId(requirementId) is { } modId && addonId?.StartsWith($"curseforge-{modId}-", StringComparison.OrdinalIgnoreCase) == true);

    public static IReadOnlyList<AddonRequirement> Of(ManagedAddon addon, AddonRelease? release) =>
        [.. (release?.Requires ?? []).Concat(addon?.Requires ?? []).OfType<AddonRequirement>().DistinctBy(requirement => requirement.Id, StringComparer.OrdinalIgnoreCase)];

    public static Task InstallAsync(ManagedAddon dependant, AddonRelease release, AddonRequirementHooks hooks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dependant);
        ArgumentNullException.ThrowIfNull(hooks);
        return WalkAsync(dependant.DisplayName, Of(dependant, release), hooks, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { dependant.Id }, 1, cancellationToken);
    }

    private static async Task WalkAsync(
        string neededBy, IReadOnlyList<AddonRequirement> requires, AddonRequirementHooks hooks, HashSet<string> seen, int depth, CancellationToken cancellationToken)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        foreach (var requirement in requires)
        {
            var addon = hooks.Resolve(requirement)
                ?? throw new AddonRequirementException($"{requirement.Name ?? requirement.Id} (needed by {neededBy}) is not available to install");
            if (!seen.Add(addon.Id) || hooks.IsSatisfied(addon, null))
            {
                continue;
            }

            try
            {
                var (channel, release) = await hooks.Fetch(addon, cancellationToken).ConfigureAwait(true)
                    ?? throw new InvalidOperationException("no build is published for this game version");
                if (hooks.IsSatisfied(addon, release))
                {
                    continue;
                }

                await WalkAsync(addon.DisplayName, Of(addon, release with { Requires = release.Requires ?? requirement.Requires }), hooks, seen, depth + 1, cancellationToken).ConfigureAwait(true);
                hooks.Status?.Invoke($"installing {addon.DisplayName} (needed by {neededBy})");
                await hooks.Install(addon, channel, release, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or AddonRequirementException or SessionExpiredException))
            {
                throw new AddonRequirementException($"Could not install {addon.DisplayName} (needed by {neededBy}): {ex.Message}", ex);
            }
        }
    }
}
