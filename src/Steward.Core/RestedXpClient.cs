using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Steward.Core;

public sealed class RestedXpSignInException(string message, IReadOnlyList<string>? responseKeys = null) : Exception(message)
{
    public IReadOnlyList<string> ResponseKeys { get; } = responseKeys ?? [];
}

public sealed class RestedXpSessionExpiredException() : Exception("Your RestedXP session expired. Sign in again.");

public sealed class RestedXpClient
{
    private readonly HttpClient _httpClient;
    private readonly string _accountBaseUrl;
    private readonly string _guidesBaseUrl;

    public RestedXpClient(HttpClient httpClient, string accountBaseUrl, string guidesBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(accountBaseUrl);
        ArgumentNullException.ThrowIfNull(guidesBaseUrl);

        _httpClient = httpClient;
        _accountBaseUrl = accountBaseUrl.TrimEnd('/');
        _guidesBaseUrl = guidesBaseUrl.TrimEnd('/');
    }

    public async Task<RestedXpLoginResponse> SignInAsync(string username, string password, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{_accountBaseUrl}/login/keycloak", null);
        request.Content = JsonContent.Create(
            new RestedXpLoginRequest(username, password), CompanionJsonContext.Default.RestedXpLoginRequest);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new RestedXpSignInException("Wrong username or password");
        }

        response.EnsureSuccessStatusCode();
        var element = await ReadElementAsync(response, cancellationToken).ConfigureAwait(false);
        if (element.ValueKind is JsonValueKind.Object && element.TryGetProperty("mfaRequired", out var mfa) && mfa.ValueKind is JsonValueKind.True)
        {
            return JsonSerializer.Deserialize(element, CompanionJsonContext.Default.RestedXpLoginResponse)!;
        }

        return ReadTokens(element) ?? throw Unexpected(element);
    }

    public async Task<RestedXpLoginResponse> VerifyMfaAsync(string sessionId, string code, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{_accountBaseUrl}/login/verify-mfa", null);
        request.Content = JsonContent.Create(
            new RestedXpMfaRequest(sessionId, code, false), CompanionJsonContext.Default.RestedXpMfaRequest);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new RestedXpSignInException("That code was rejected.");
        }

        response.EnsureSuccessStatusCode();
        var element = await ReadElementAsync(response, cancellationToken).ConfigureAwait(false);
        return ReadTokens(element) ?? throw Unexpected(element);
    }

    public async Task<RestedXpLoginResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{_accountBaseUrl}/login/keycloak/refresh", null);
        request.Content = JsonContent.Create(
            new RestedXpRefreshRequest(refreshToken), CompanionJsonContext.Default.RestedXpRefreshRequest);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new RestedXpSessionExpiredException();
        }

        response.EnsureSuccessStatusCode();
        var element = await ReadElementAsync(response, cancellationToken).ConfigureAwait(false);
        return ReadTokens(element) ?? throw Unexpected(element);
    }

    public async Task<IReadOnlyList<RestedXpProduct>> GetProductsAsync(RestedXpSession session, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Get, $"{_accountBaseUrl}/user-products", session);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            throw new RestedXpSessionExpiredException();
        }

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.RestedXpProductArray, cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    public async Task<IReadOnlyDictionary<string, long>> GetTimestampsAsync(CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Get, $"{_guidesBaseUrl}/addon/get-all-timestamps", null);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var timestamps = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.RestedXpTimestamps, cancellationToken)
            .ConfigureAwait(false);
        return timestamps?.Timestamps ?? new Dictionary<string, long>();
    }

    public async Task<RestedXpGuide> DownloadGuideAsync(RestedXpSession session, string productName, CancellationToken cancellationToken)
    {
        var url = $"{_guidesBaseUrl}/addon?guideName={Uri.EscapeDataString(productName)}&bundleIndex=0";
        using var request = Build(HttpMethod.Get, url, session);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            throw new RestedXpSessionExpiredException();
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.RestedXpGuideResponse, cancellationToken)
            .ConfigureAwait(false);

        return payload?.EncryptedGuides.FirstOrDefault()
            ?? throw new HttpRequestException($"RestedXP returned no guide for {productName}.");
    }

    public static string BuildCookie(RestedXpSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var value = JsonSerializer.Serialize(
            new RestedXpCookie(session.AccessToken, session.RefreshToken, [], session.RefreshExpiresAt.ToUnixTimeMilliseconds()),
            CompanionJsonContext.Default.RestedXpCookie);
        return $"rxp_cross_auth_token={Uri.EscapeDataString(value)}";
    }

    private static HttpRequestMessage Build(HttpMethod method, string url, RestedXpSession? session)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Steward", "1"));
        if (session is not null)
        {
            request.Headers.Add("Cookie", BuildCookie(session));
        }

        return request;
    }

    private static async Task<JsonElement> ReadElementAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync(CompanionJsonContext.Default.JsonElement, cancellationToken).ConfigureAwait(false);

    private static RestedXpLoginResponse? ReadTokens(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("access_token", out _))
        {
            return JsonSerializer.Deserialize(element, CompanionJsonContext.Default.RestedXpLoginResponse);
        }

        if (element.TryGetProperty("token", out var token)
            && token.ValueKind is JsonValueKind.Object
            && token.TryGetProperty("access_token", out _))
        {
            return JsonSerializer.Deserialize(token, CompanionJsonContext.Default.RestedXpLoginResponse);
        }

        return null;
    }

    private static RestedXpSignInException Unexpected(JsonElement element) => new(
        "Sign-in returned an unexpected response",
        element.ValueKind is JsonValueKind.Object
            ? [.. element.EnumerateObject().Select(property => property.Name)]
            : [element.ValueKind.ToString()]);
}
