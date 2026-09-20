using System.Net;
using System.Text;

namespace Steward.Core.Tests;

public sealed class RestedXpClientTests
{
    private const string TokenUrl = "https://sso.example.com/realms/Test/protocol/openid-connect/token";

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        public string? RequestUrl { get; private set; }

        public bool HadCookie { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUrl = request.RequestUri?.ToString();
            HadCookie = request.Headers.Contains("Cookie");
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (RestedXpClient Client, StubHandler Handler) ClientFor(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (
            new RestedXpClient(
                new HttpClient(handler),
                "https://account.example.com",
                "https://guides.example.com",
                TokenUrl,
                "test-client",
                "openid offline_access"),
            handler);
    }

    private static Dictionary<string, string> Form(string body) => body
        .Split('&')
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')), StringComparer.Ordinal);

    private const string OfflineBody =
        """{"access_token":"access","refresh_token":"refresh","expires_in":900,"refresh_expires_in":0,"token_type":"Bearer","scope":"openid offline_access"}""";

    [Fact]
    public async Task SignInAsync_OfflineToken_HasNoRefreshExpiry()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, OfflineBody);

        var tokens = await client.SignInAsync("hoobi", "hunter2", null, CancellationToken.None);

        Assert.Equal("access", tokens.AccessToken);
        Assert.Equal("refresh", tokens.RefreshToken);
        Assert.Null(tokens.RefreshExpiresAt);
        Assert.InRange(tokens.AccessExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(890), TimeSpan.FromSeconds(900));
    }

    [Fact]
    public async Task SignInAsync_BoundedRefreshToken_HasRefreshExpiry()
    {
        var (client, _) = ClientFor(
            HttpStatusCode.OK,
            """{"access_token":"access","refresh_token":"refresh","expires_in":900,"refresh_expires_in":1800}""");

        var tokens = await client.SignInAsync("hoobi", "hunter2", null, CancellationToken.None);

        Assert.NotNull(tokens.RefreshExpiresAt);
        Assert.InRange(
            tokens.RefreshExpiresAt.Value - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1790), TimeSpan.FromSeconds(1800));
    }

    [Fact]
    public async Task SignInAsync_PasswordGrantBody_CarriesCredentialsAndScopeWithoutTotp()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, OfflineBody);

        await client.SignInAsync("hoobi", "hunter2", null, CancellationToken.None);

        var form = Form(handler.RequestBody!);
        Assert.Equal(TokenUrl, handler.RequestUrl);
        Assert.False(handler.HadCookie);
        Assert.Equal("password", form["grant_type"]);
        Assert.Equal("test-client", form["client_id"]);
        Assert.Equal("openid offline_access", form["scope"]);
        Assert.Equal("hoobi", form["username"]);
        Assert.Equal("hunter2", form["password"]);
        Assert.False(form.ContainsKey("totp"));
    }

    [Fact]
    public async Task SignInAsync_WithCode_SendsTotp()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, OfflineBody);

        await client.SignInAsync("hoobi", "hunter2", "123456", CancellationToken.None);

        Assert.Equal("123456", Form(handler.RequestBody!)["totp"]);
    }

    [Fact]
    public async Task RefreshAsync_Body_CarriesRefreshTokenOnly()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, OfflineBody);

        await client.RefreshAsync("stored-refresh", CancellationToken.None);

        var form = Form(handler.RequestBody!);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("test-client", form["client_id"]);
        Assert.Equal("stored-refresh", form["refresh_token"]);
        Assert.False(form.ContainsKey("username"));
        Assert.False(form.ContainsKey("password"));
        Assert.False(form.ContainsKey("scope"));
    }

    [Fact]
    public async Task SignInAsync_InvalidCredentials_ThrowsSignInException()
    {
        var (client, _) = ClientFor(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Invalid user credentials"}""");

        var exception = await Assert.ThrowsAsync<RestedXpSignInException>(
            () => client.SignInAsync("hoobi", "wrong", null, CancellationToken.None));

        Assert.Equal("Wrong username or password", exception.Message);
    }

    [Theory]
    [InlineData("Invalid OTP")]
    [InlineData("Account is not fully set up")]
    public async Task SignInAsync_OtpDescription_ThrowsMfaRequired(string description)
    {
        var (client, _) = ClientFor(
            HttpStatusCode.BadRequest,
            $$"""{"error":"invalid_grant","error_description":"{{description}}"}""");

        await Assert.ThrowsAsync<RestedXpMfaRequiredException>(
            () => client.SignInAsync("hoobi", "hunter2", null, CancellationToken.None));
    }

    [Fact]
    public async Task SignInAsync_OtherError_KeepsKeycloakDescription()
    {
        var (client, _) = ClientFor(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_client","error_description":"Invalid client credentials"}""");

        var exception = await Assert.ThrowsAsync<RestedXpSignInException>(
            () => client.SignInAsync("hoobi", "hunter2", null, CancellationToken.None));

        Assert.Equal("Invalid client credentials", exception.Message);
    }

    [Fact]
    public async Task RefreshAsync_BadRequest_ThrowsSessionExpired()
    {
        var (client, _) = ClientFor(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Token is not active"}""");

        await Assert.ThrowsAsync<RestedXpSessionExpiredException>(
            () => client.RefreshAsync("stale", CancellationToken.None));
    }

    [Fact]
    public void BuildCookie_NoRefreshExpiry_ExpiresIn30Days()
    {
        var session = new RestedXpSession("hoobi", "access", "refresh", DateTimeOffset.UtcNow.AddMinutes(15), null);

        var cookie = Uri.UnescapeDataString(RestedXpClient.BuildCookie(session)["rxp_cross_auth_token=".Length..]);

        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(
            long.Parse(cookie.Split("\"expiresAt\":")[1].TrimEnd('}'), System.Globalization.CultureInfo.InvariantCulture));
        Assert.InRange(expiresAt - DateTimeOffset.UtcNow, TimeSpan.FromDays(29.9), TimeSpan.FromDays(30));
    }
}
