using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Steward.Core;

public sealed class SessionExpiredException() : Exception("The session has expired or been revoked.");

public sealed class StewardRequestException(HttpStatusCode statusCode, string? body)
    : Exception($"request failed with {(int)statusCode} {statusCode}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? Body { get; } = body;
}

public sealed class StewardThrottledException(TimeSpan? retryAfter = null) : Exception("request was rate limited (429)")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class StewardClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public StewardClient(HttpClient httpClient, string baseUrl, string userAgent)
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
                $"GET /api/me returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
        }

        var me = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.AdminMe, cancellationToken)
            .ConfigureAwait(false);

        return me is null
            ? throw new HttpRequestException("GET /api/me returned an empty body")
            : me with { Addons = me.Addons is null ? null : [.. me.Addons.Where(AddonCatalogue.IsComplete)] };
    }

    // Works signed in or out, so a 401 is not a lost session here; the caller keeps its last good banner set on any failure.
    public async Task<IReadOnlyList<Banner>> GetBannersAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{_baseUrl}/api/banners", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GET /api/banners returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return BannerParsing.Parse(json);
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
            throw new StewardRequestException(response.StatusCode, null);
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
            throw new StewardRequestException(response.StatusCode, null);
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
            throw new StewardRequestException(response.StatusCode, null);
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
                $"GET /api/admin/{guildId}/roster returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
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
                $"GET /api/admin/{guildId}/members returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
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
            throw new StewardRequestException(response.StatusCode, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        await foreach (var eventType in ServerSentEventReader.ReadEventsAsync(reader, EventStreamIdleTimeout, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return eventType;
        }
    }

    public async IAsyncEnumerable<string> StreamAccessEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new StewardRequestException(response.StatusCode, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        await foreach (var frame in ServerSentEventReader.ReadFramesAsync(reader, EventStreamIdleTimeout, cancellationToken)
            .ConfigureAwait(false))
        {
            // /api/events carries its frame type inside the JSON data payload rather than an SSE `event:` field.
            if (frame.Data is null)
            {
                continue;
            }

            AccessEventFrame? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize(frame.Data, CompanionJsonContext.Default.AccessEventFrame);
            }
            catch (JsonException)
            {
                continue;
            }

            if (parsed?.Type is { Length: > 0 } type)
            {
                yield return type;
            }
        }
    }

    public async Task SetSelectedGuildAsync(string guildId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PutAsJsonAsync(
                $"{_baseUrl}/api/me/guild",
                new SelectedGuildRequest(guildId),
                CompanionJsonContext.Default.SelectedGuildRequest,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new StewardRequestException(response.StatusCode, null);
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
                $"POST /api/auth/desktop/exchange returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
        }

        var token = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.DesktopToken, cancellationToken)
            .ConfigureAwait(false);

        return token?.Token
            ?? throw new HttpRequestException("POST /api/auth/desktop/exchange returned an empty body");
    }

    public const string GuidesFeature = "guides";
    public const string StewardFeature = "steward";
    public const string SyncFeature = "sync";
    public const string RosterFeature = "roster";
    public const string ProfessionsFeature = "professions";
    public const string SignupsFeature = "signups";
    public const string HoobiScriptsFeature = "hoobiscripts";
    public const string AddonsFeature = "addons";

    private static readonly IReadOnlySet<string> OfficerFeatures = new HashSet<string>(
        [GuidesFeature, StewardFeature, SyncFeature, RosterFeature, ProfessionsFeature, SignupsFeature, AddonsFeature],
        StringComparer.Ordinal);

    // signups alone gives no in-app access today, so it does not authorize; hoobiscripts and addons are each grantable to anyone and authorize on their own.
    private static readonly IReadOnlySet<string> AuthorizingFeatures = new HashSet<string>(
        [GuidesFeature, StewardFeature, SyncFeature, RosterFeature, ProfessionsFeature, HoobiScriptsFeature, AddonsFeature],
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

    // steward/sync/roster/professions gate per guild, since an officer of one guild is a plain member of another; a guild entry with no features (an older Steward API) falls back to the user-level set.
    public static IReadOnlySet<string> ResolveGuildFeatures(AdminGuild? guild, IReadOnlySet<string> userFeatures) =>
        guild?.Features is { } features ? new HashSet<string>(features, StringComparer.Ordinal) : userFeatures;

    public string CurseForgeManifestBaseUrl(int modId, int versionType) =>
        $"{_baseUrl}/api/addons/curseforge/{modId}/{versionType}/";

    public async Task<IReadOnlyList<CurseForgeResult>> GetCurseForgeDiscoverAsync(int versionType, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/addons/curseforge/discover?versionType={versionType}");
        var discover = await SendCurseForgeAsync(request, CompanionJsonContext.Default.CurseForgeDiscover, cancellationToken).ConfigureAwait(false);
        return [.. (discover.Popular ?? []).Concat(discover.RecentlyUpdated ?? []).DistinctBy(result => result.Id)];
    }

    public async Task<IReadOnlyList<CurseForgeResult>?> SearchCurseForgeAsync(int versionType, string query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl}/api/addons/curseforge/search?versionType={versionType}&q={Uri.EscapeDataString(query)}");
        try
        {
            return (await SendCurseForgeAsync(request, CompanionJsonContext.Default.CurseForgeSearch, cancellationToken).ConfigureAwait(false)).Results ?? [];
        }
        catch (StewardRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable && ex.Body?.Contains("search_unavailable", StringComparison.Ordinal) == true)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<CurseForgeMatch>> MatchCurseForgeAsync(int versionType, CurseForgeMatchRequest match, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/addons/curseforge/match?versionType={versionType}")
        {
            Content = JsonContent.Create(match, CompanionJsonContext.Default.CurseForgeMatchRequest),
        };
        return await SendCurseForgeAsync(request, CompanionJsonContext.Default.CurseForgeMatchArray, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportCurseForgeDownloadFailureAsync(CurseForgeDownloadFailure failure, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsync($"{_baseUrl}/api/addons/curseforge/download-failure", JsonContent.Create(failure, CompanionJsonContext.Default.CurseForgeDownloadFailure), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CurseForgeModFile?> GetCurseForgeFileAsync(int modId, long fileId, CancellationToken cancellationToken, int? versionType = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/addons/curseforge/{modId}/files/{fileId}{(versionType is { } type ? $"?versionType={type}" : "")}");
        try
        {
            return await SendCurseForgeAsync(request, CompanionJsonContext.Default.CurseForgeModFile, cancellationToken).ConfigureAwait(false);
        }
        catch (StewardRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<CurseForgeLatestFile>?> GetCurseForgeLatestFilesAsync(int modId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/addons/curseforge/{modId}/latest-files");
        try
        {
            return (await SendCurseForgeAsync(request, CompanionJsonContext.Default.CurseForgeLatestFiles, cancellationToken).ConfigureAwait(false)).Files ?? [];
        }
        catch (StewardRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string?> GetCurseForgeIconUrlAsync(int modId, int versionType, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"{CurseForgeManifestBaseUrl(modId, versionType)}icon.png", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var landed = response.RequestMessage?.RequestUri;
        return response.IsSuccessStatusCode && landed is not null && !landed.AbsoluteUri.StartsWith(_baseUrl, StringComparison.OrdinalIgnoreCase)
            ? landed.AbsoluteUri
            : null;
    }

    private async Task<T> SendCurseForgeAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new StewardRequestException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException($"{request.Method} {request.RequestUri?.AbsolutePath} returned an empty body");
    }

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
                $"GET /api/admin/{guildId}/recipes/catalogue returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
        }

        var body = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.RecipeCatalogueResponse, cancellationToken)
            .ConfigureAwait(false);

        return body?.Catalogue is { } catalogue
            ? MemberCatalogueMapping.ToCatalogue(catalogue)
            : new Dictionary<string, IReadOnlyList<CatalogueRecipe>>();
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
            .PostAsync($"{_baseUrl}/api/sync/{guildId}/characters", content, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            // 429 means the server is up and throttling this route specifically, distinct from a 408/5xx "Steward API is unreachable" transient; every other 4xx rejects this batch outright, so retrying it unchanged would never succeed.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new StewardThrottledException();
            }

            if ((int)response.StatusCode is >= 400 and < 500 and not 408)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new StewardRequestException(response.StatusCode, body);
            }

            throw new HttpRequestException(
                $"POST /api/sync/{guildId}/characters returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
        }

        var result = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.CharacterSyncResponse, cancellationToken)
            .ConfigureAwait(false);

        return result ?? throw new HttpRequestException(
            $"POST /api/sync/{guildId}/characters returned an empty body");
    }

    public async Task<JourneyUploadResponse> PutJourneyAsync(
        string account, string realm, string character, byte[] file, JourneySummary summary, long mtime, CancellationToken cancellationToken)
    {
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            await gzip.WriteAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var path = $"/api/sync/journey/{Uri.EscapeDataString(account)}/{Uri.EscapeDataString(realm)}/{Uri.EscapeDataString(character)}";
        using var request = new HttpRequestMessage(HttpMethod.Put, _baseUrl + path)
        {
            Content = new ByteArrayContent(compressed.ToArray()),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("gzip");
        request.Headers.Add("X-Journey-Rows", summary.Rows.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Journey-Mtime", mtime.ToString(CultureInfo.InvariantCulture));
        if (summary.First is { } first)
        {
            request.Headers.Add("X-Journey-First", first.ToString(CultureInfo.InvariantCulture));
        }

        if (summary.Last is { } last)
        {
            request.Headers.Add("X-Journey-Last", last.ToString(CultureInfo.InvariantCulture));
        }

        if (summary.CharacterGuid is { } guid)
        {
            request.Headers.Add("X-Journey-Guid", guid);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SessionExpiredException();
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter;
            throw new StewardThrottledException(retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow));
        }

        if ((int)response.StatusCode is >= 400 and < 500 and not 408)
        {
            throw new StewardRequestException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"PUT {path} returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync(CompanionJsonContext.Default.JourneyUploadResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException($"PUT {path} returned an empty body");
    }
}
