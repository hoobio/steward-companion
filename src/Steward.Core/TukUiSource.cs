using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Steward.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Steward.Core;

public sealed record TukUiAddon(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("last_update")] string? LastUpdate,
    [property: JsonPropertyName("changelog_url")] string? ChangelogUrl,
    [property: JsonPropertyName("web_url")] string? WebUrl,
    [property: JsonPropertyName("logo_square_url")] string? LogoSquareUrl,
    [property: JsonPropertyName("small_desc")] string? SmallDesc,
    [property: JsonPropertyName("directories")] IReadOnlyList<string>? Directories,
    [property: JsonPropertyName("patch")] IReadOnlyList<string>? Patch);

public sealed class TukUiSource(HttpClient http, TimeProvider? clock = null, ILogger? logger = null)
{
    public static readonly Uri ListUri = new("https://api.tukui.org/v1/addons");
    public static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshMaxAge = TimeSpan.FromSeconds(60);

    // A list's download token answered 403 by 16 minutes old (26 Oct 2026), so a download takes its token from a list this fresh.
    public static readonly TimeSpan TokenMaxAge = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly Lock _fetchLock = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _notes = new(StringComparer.Ordinal);
    private Listing? _list;
    private Task<IReadOnlyList<TukUiAddon>>? _fetch;

    public TukUiAddon? Cached(int projectId) => _list?.Addons.FirstOrDefault(addon => addon.Id == projectId);

    public Task<IReadOnlyList<TukUiAddon>> ListAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        if (Fresh(maxAge) is { } hit)
        {
            return Task.FromResult(hit);
        }

        lock (_fetchLock)
        {
            _fetch = _fetch is { IsCompleted: false } running ? running : FetchAsync();
            return _fetch.WaitAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<TukUiAddon>> FetchAsync()
    {
        IReadOnlyList<TukUiAddon> addons = await http.GetFromJsonAsync(ListUri, CompanionJsonContext.Default.TukUiAddonArray).ConfigureAwait(false)
            ?? throw new JsonException("TukUI answered null for its addon list");
        _list = new Listing(_clock.GetUtcNow(), addons);
        return addons;
    }

    private sealed record Listing(DateTimeOffset Fetched, IReadOnlyList<TukUiAddon> Addons);

    private IReadOnlyList<TukUiAddon>? Fresh(TimeSpan maxAge) =>
        _list is { } list && _clock.GetUtcNow() - list.Fetched < maxAge ? list.Addons : null;

    public async Task<AddonRelease?> ReleaseAsync(int projectId, bool noCache, CancellationToken cancellationToken)
    {
        var addon = (await ListAsync(noCache ? RefreshMaxAge : ListTtl, cancellationToken).ConfigureAwait(false)).FirstOrDefault(listed => listed.Id == projectId);
        return addon is null ? null : ToRelease(addon, await NotesAsync(addon, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Uri> ZipUriAsync(int projectId, string version, CancellationToken cancellationToken)
    {
        var addon = (await ListAsync(TokenMaxAge, cancellationToken).ConfigureAwait(false)).FirstOrDefault(listed => listed.Id == projectId)
            ?? throw new InvalidOperationException($"TukUI no longer lists project {projectId}");
        if (!string.Equals(DisplayVersion(addon), version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"TukUI now lists {addon.Name} {DisplayVersion(addon)}, not {version}; refresh and try again");
        }

        return Https(addon.Url) is { } url ? new Uri(url) : throw new InvalidOperationException($"TukUI lists {addon.Name} with no https download");
    }

    public static AddonRelease ToRelease(TukUiAddon addon, IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(addon);
        if (string.IsNullOrWhiteSpace(addon.Version)
            || !DateOnly.TryParseExact(addon.LastUpdate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var released))
        {
            throw new JsonException($"TukUI lists project {addon.Id} with no version or release date");
        }

        return new AddonRelease(
            DisplayVersion(addon),
            Https(addon.Url),
            null,
            0,
            new DateTimeOffset(released, TimeOnly.MinValue, TimeSpan.Zero),
            notes,
            addon.Directories,
            Https(addon.WebUrl))
        {
            TukUiProjectId = addon.Id,
        };
    }

    public static string DisplayVersion(TukUiAddon addon) => addon?.Version?.Trim().TrimStart('v') ?? "";

    public static bool RunsOn(TukUiAddon addon, string? clientVersion)
    {
        ArgumentNullException.ThrowIfNull(addon);
        var major = clientVersion?.Split('.')[0];
        return addon.Patch is not { Count: > 0 } patches || string.IsNullOrEmpty(major)
            || patches.Any(patch => string.Equals(patch.Split('.')[0], major, StringComparison.Ordinal));
    }

    public static IReadOnlyList<string> Notes(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var lines = markdown.Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith("### ", StringComparison.Ordinal));
        if (start < 0)
        {
            return [];
        }

        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("### ", StringComparison.Ordinal));
        return [.. lines[(start + 1)..(end < 0 ? lines.Length : end)]
            .Select(line => line.Replace("**", "", StringComparison.Ordinal).Trim().TrimStart('*', '-').Trim())
            .Where(line => line.Length > 0)];
    }

    public static string? Https(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;

    private async Task<IReadOnlyList<string>> NotesAsync(TukUiAddon addon, CancellationToken cancellationToken)
    {
        var key = $"{addon.Id}|{addon.Version}";
        if (Https(addon.ChangelogUrl) is not { } url)
        {
            return [];
        }

        if (_notes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var notes = Notes(await http.GetStringAsync(new Uri(url), cancellationToken).ConfigureAwait(false));
            _notes[key] = notes;
            return notes;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.Warn(ex, $"TukUI changelog for {addon.Name} {addon.Version} failed; showing no notes until the next check");
            return [];
        }
    }
}
