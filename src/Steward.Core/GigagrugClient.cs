using System.Net;
using System.Net.Http.Json;

namespace Steward.Core;

public sealed class SessionExpiredException() : Exception("The session has expired or been revoked.");

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

    public static bool IsAdmin(AdminMe me) =>
        me.User.Role is "global" or "admin";

    public static bool IsGlobalAdmin(AdminMe me) =>
        me.User.Role is "global";
}
