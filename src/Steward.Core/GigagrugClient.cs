using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
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

    public GigagrugClient(HttpClient httpClient, string baseUrl, string userAgent)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public async Task<AdminMe> GetMeAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/me", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GET /api/me returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var me = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.AdminMe, cancellationToken)
            .ConfigureAwait(false);

        return me ?? throw new HttpRequestException("GET /api/me returned an empty body");
    }

    public async Task<MemberRoster> GetMemberRosterAsync(string guildId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/guild/{guildId}/roster", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GigagrugRequestException(response.StatusCode, null);
        }

        var roster = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.MemberRoster, cancellationToken)
            .ConfigureAwait(false);

        return roster ?? throw new HttpRequestException($"GET /api/guild/{guildId}/roster returned an empty body");
    }

    public async Task<MemberProfessions> GetMemberProfessionsAsync(string guildId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/guild/{guildId}/professions", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GigagrugRequestException(response.StatusCode, null);
        }

        var professions = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.MemberProfessions, cancellationToken)
            .ConfigureAwait(false);

        return professions ?? throw new HttpRequestException($"GET /api/guild/{guildId}/professions returned an empty body");
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DirectoryRecipe>>> GetMemberCatalogueAsync(string guildId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/guild/{guildId}/recipes/catalogue", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GigagrugRequestException(response.StatusCode, null);
        }

        var catalogue = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.MemberCatalogue, cancellationToken)
            .ConfigureAwait(false);

        return catalogue?.Catalogue ?? throw new HttpRequestException($"GET /api/guild/{guildId}/recipes/catalogue returned an empty body");
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

    private static readonly TimeSpan EventStreamIdleTimeout = TimeSpan.FromSeconds(60);

    public async IAsyncEnumerable<string> StreamGuildEventsAsync(
        string guildId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/admin/{guildId}/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // HttpClient.Timeout covers only SendAsync up to the headers with ResponseHeadersRead; the body read is bounded by the idle timeout instead.
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GigagrugRequestException(response.StatusCode, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        await foreach (var eventType in ServerSentEventReader.ReadEventsAsync(reader, EventStreamIdleTimeout, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return eventType;
        }
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
    public const string RosterFeature = "roster";
    public const string ProfessionsFeature = "professions";
    public const string SignupsFeature = "signups";

    private static readonly IReadOnlySet<string> OfficerFeatures = new HashSet<string>(
        [AddonsFeature, GuidesFeature, StewardFeature, SyncFeature, RosterFeature, ProfessionsFeature, SignupsFeature],
        StringComparer.Ordinal);

    // signups alone gives no in-app access today, so it does not authorize.
    private static readonly IReadOnlySet<string> AuthorizingFeatures = new HashSet<string>(
        [AddonsFeature, GuidesFeature, StewardFeature, SyncFeature, RosterFeature, ProfessionsFeature],
        StringComparer.Ordinal);

    public static bool IsAdmin(AdminMe me) =>
        me.User.Role is "global" or "admin";

    public static bool IsGlobalAdmin(AdminMe me) =>
        me.User.Role is "global";

    public static IReadOnlySet<string> EffectiveFeatures(AdminMe me) =>
        me.User.Features is { } features
            ? new HashSet<string>(features, StringComparer.Ordinal)
            : IsAdmin(me) ? OfficerFeatures : new HashSet<string>(StringComparer.Ordinal);

    public static bool IsAuthorizing(IReadOnlySet<string> features) =>
        AuthorizingFeatures.Any(features.Contains);

    // steward/sync/roster/professions gate per guild, since an officer of one guild is a plain member of another; a guild entry with no features (an older gigagrug, or /api/admin/me) falls back to the user-level set.
    public static IReadOnlySet<string> ResolveGuildFeatures(AdminGuild? guild, IReadOnlySet<string> userFeatures) =>
        guild?.Features is { } features ? new HashSet<string>(features, StringComparer.Ordinal) : userFeatures;

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
            .PostAsync($"{_baseUrl}/api/guild/{guildId}/characters/sync", content, cancellationToken)
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
                $"POST /api/guild/{guildId}/characters/sync returned {(int)response.StatusCode} {response.StatusCode}");
        }

        var result = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.CharacterSyncResponse, cancellationToken)
            .ConfigureAwait(false);

        return result ?? throw new HttpRequestException(
            $"POST /api/guild/{guildId}/characters/sync returned an empty body");
    }
}
