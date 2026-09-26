using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Steward.Core;

public sealed class SessionExpiredException() : Exception("The session has expired or been revoked.");

public sealed class GigagrugRequestException(HttpStatusCode statusCode, string? body)
    : Exception($"request failed with {(int)statusCode} {statusCode}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? Body { get; } = body;
}

public sealed class GigagrugClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public GigagrugClient(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<AdminMe> GetMeAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/admin/me", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GET /api/admin/me returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var me = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.AdminMe, cancellationToken)
            .ConfigureAwait(false);

        return me ?? throw new HttpRequestException("GET /api/admin/me returned an empty body");
    }

    public async Task<(IReadOnlyList<GuildRosterMember> Members, IReadOnlyList<string> Statuses, IReadOnlyList<OriginDef> Origins)> GetGuildRosterAsync(string guildId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/admin/{guildId}/roster", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GET /api/admin/{guildId}/roster returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var roster = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.GuildRosterResponse, cancellationToken)
            .ConfigureAwait(false);

        if (roster is null)
        {
            throw new HttpRequestException($"GET /api/admin/{guildId}/roster returned an empty body");
        }

        IReadOnlyList<GuildRosterMember> members =
            [.. roster.Members.Select(member => member with { Notes = RosterNotes.Trim(member.Notes) })];
        IReadOnlyList<string> statuses = roster.Statuses is null ? [] : [.. roster.Statuses.Select(status => status.Name)];
        IReadOnlyList<OriginDef> origins = roster.Origins ?? [];
        return (members, statuses, origins);
    }

    public async Task<IReadOnlyList<DiscordMember>> GetDiscordMembersAsync(string guildId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/admin/{guildId}/members", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GET /api/admin/{guildId}/members returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var members = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.DiscordMembersResponse, cancellationToken)
            .ConfigureAwait(false);

        return members?.Members
            ?? throw new HttpRequestException($"GET /api/admin/{guildId}/members returned an empty body");
    }

    public async Task<string> ExchangeDesktopCodeAsync(string code, string verifier, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsJsonAsync(
                $"{_baseUrl}/api/auth/desktop/exchange",
                new DesktopExchangeRequest(code, verifier),
                CompanionJsonContext.Default.DesktopExchangeRequest,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException("The sign-in code was rejected or has expired.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"POST /api/auth/desktop/exchange returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var token = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.DesktopToken, cancellationToken)
            .ConfigureAwait(false);

        return token?.Token
            ?? throw new HttpRequestException("POST /api/auth/desktop/exchange returned an empty body");
    }

    public const string AddonsFeature = "addons";
    public const string GuidesFeature = "guides";
    public const string StewardFeature = "steward";
    public const string SyncFeature = "sync";

    private static readonly IReadOnlySet<string> AllFeatures = new HashSet<string>(
        [AddonsFeature, GuidesFeature, StewardFeature], StringComparer.Ordinal);

    public static bool IsAdmin(AdminMe me) =>
        me.User.Role is "global" or "admin";

    public static bool IsGlobalAdmin(AdminMe me) =>
        me.User.Role is "global";

    public static IReadOnlySet<string> EffectiveFeatures(AdminMe me) =>
        me.User.Features is { } features
            ? new HashSet<string>(features, StringComparer.Ordinal)
            : IsAdmin(me) ? AllFeatures : new HashSet<string>(StringComparer.Ordinal);

    // sync is never standalone: a user holding only it has nothing else to do in the app.
    public static bool IsAuthorizing(IReadOnlySet<string> features) =>
        AllFeatures.Any(features.Contains);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<CatalogueRecipe>>> GetRecipeCatalogueAsync(
        string guildId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/admin/{guildId}/recipes/catalogue", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GET /api/admin/{guildId}/recipes/catalogue returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var body = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.RecipeCatalogueResponse, cancellationToken)
            .ConfigureAwait(false);

        return body?.Catalogue ?? new Dictionary<string, IReadOnlyList<CatalogueRecipe>>();
    }

    public async Task<CharacterSyncResponse> PostCharacterSyncAsync(
        string guildId, CharacterSyncRequest batch, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(batch, CompanionJsonContext.Default.CharacterSyncRequest);
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            await gzip.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        }

        using var content = new ByteArrayContent(compressed.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");

        using var response = await _httpClient
            .PostAsync($"{_baseUrl}/api/admin/{guildId}/characters/sync", content, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            // 429/408 are transient like a 5xx; every other 4xx is the server rejecting this batch outright, so retrying it unchanged would never succeed.
            if ((int)response.StatusCode is >= 400 and < 500 and not 429 and not 408)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new GigagrugRequestException(response.StatusCode, body);
            }

            throw new HttpRequestException(
                $"POST /api/admin/{guildId}/characters/sync returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var result = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.CharacterSyncResponse, cancellationToken)
            .ConfigureAwait(false);

        return result ?? throw new HttpRequestException(
            $"POST /api/admin/{guildId}/characters/sync returned an empty body");
    }
}
