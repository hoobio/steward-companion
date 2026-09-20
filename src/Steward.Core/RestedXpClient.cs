using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Steward.Core;

public sealed class RestedXpSignInException(string message) : Exception(message);

public sealed class RestedXpMfaRequiredException() : Exception("RestedXP asked for an authenticator code.");

public sealed class RestedXpSessionExpiredException() : Exception("Your RestedXP session expired. Sign in again.");

public sealed class RestedXpClient
{
    private static readonly TimeSpan OfflineCookieLifetime = TimeSpan.FromDays(30);

    private readonly HttpClient _httpClient;
    private readonly string _accountBaseUrl;
    private readonly string _guidesBaseUrl;
    private readonly string _tokenUrl;
    private readonly string _clientId;
    private readonly string _scope;

    public RestedXpClient(
        HttpClient httpClient, string accountBaseUrl, string guidesBaseUrl, string tokenUrl, string clientId, string scope)
    {
        ArgumentNullException.ThrowIfNull(accountBaseUrl);
        ArgumentNullException.ThrowIfNull(guidesBaseUrl);
        ArgumentNullException.ThrowIfNull(tokenUrl);
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(scope);

        _httpClient = httpClient;
        _accountBaseUrl = accountBaseUrl.TrimEnd('/');
        _guidesBaseUrl = guidesBaseUrl.TrimEnd('/');
        _tokenUrl = tokenUrl;
        _clientId = clientId;
        _scope = scope;
    }

    public Task<RestedXpTokens> SignInAsync(string username, string password, string? code, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["client_id"] = _clientId,
            ["scope"] = _scope,
            ["username"] = username,
            ["password"] = password,
        };

        if (!string.IsNullOrWhiteSpace(code))
        {
            form["totp"] = code;
        }

        return TokenAsync(form, refreshing: false, cancellationToken);
    }

    public Task<RestedXpTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => TokenAsync(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _clientId,
            ["refresh_token"] = refreshToken,
        },
        refreshing: true,
        cancellationToken);

    private async Task<RestedXpTokens> TokenAsync(
        Dictionary<string, string> form, bool refreshing, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, _tokenUrl, null);
        request.Content = new FormUrlEncodedContent(form);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw refreshing
                ? new RestedXpSessionExpiredException()
                : await SignInFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content
            .ReadFromJsonAsync(CompanionJsonContext.Default.RestedXpTokenResponse, cancellationToken)
            .ConfigureAwait(false);

        if (tokens?.AccessToken is not { } accessToken || tokens.RefreshToken is not { } refreshToken)
        {
            throw new RestedXpSignInException("Sign-in returned an unexpected response");
        }

        var now = DateTimeOffset.UtcNow;
        return new RestedXpTokens(
            accessToken,
            refreshToken,
            now.AddSeconds(tokens.ExpiresIn),
            tokens.RefreshExpiresIn > 0 ? now.AddSeconds(tokens.RefreshExpiresIn) : null);
    }

    private static async Task<Exception> SignInFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        RestedXpKeycloakError? error;
        try
        {
            error = await response.Content
                .ReadFromJsonAsync(CompanionJsonContext.Default.RestedXpKeycloakError, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            error = null;
        }

        var description = error?.ErrorDescription ?? string.Empty;
        if (!string.Equals(error?.Error, "invalid_grant", StringComparison.Ordinal))
        {
            return new RestedXpSignInException(description.Length > 0 ? description : "Sign-in failed");
        }

        if (description.Contains("otp", StringComparison.OrdinalIgnoreCase)
            || description.Contains("not fully set up", StringComparison.OrdinalIgnoreCase))
        {
            return new RestedXpMfaRequiredException();
        }

        return new RestedXpSignInException(
            description.Contains("Invalid user credentials", StringComparison.OrdinalIgnoreCase)
                ? "Wrong username or password"
                : description.Length > 0 ? description : "Sign-in failed");
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

        var expiresAt = session.RefreshExpiresAt ?? DateTimeOffset.UtcNow.Add(OfflineCookieLifetime);
        var value = JsonSerializer.Serialize(
            new RestedXpCookie(session.AccessToken, session.RefreshToken, [], expiresAt.ToUnixTimeMilliseconds()),
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

}
