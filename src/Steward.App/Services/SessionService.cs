using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using Steward.Core;

namespace Steward.App.Services;

public sealed class SessionService : ISessionService
{
    private const string SessionCookieName = "gg_session";
    private const string CallbackBody =
        "<!doctype html><title>Steward</title>" +
        "<p style=\"font-family:Segoe UI Variable,Segoe UI;margin:3rem\">Signed in. You can close this tab.</p>";

    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly CookieContainer _cookieContainer;
    private readonly AppStateStore _stateStore;
    private readonly GigagrugClient _gigagrugClient;
    private readonly string _baseUrl;

    public SessionService(
        CookieContainer cookieContainer,
        AppStateStore stateStore,
        GigagrugClient gigagrugClient,
        string baseUrl)
    {
        _cookieContainer = cookieContainer;
        _stateStore = stateStore;
        _gigagrugClient = gigagrugClient;
        _baseUrl = baseUrl;
    }

    public bool TryRestoreSession()
    {
        var token = _stateStore.Load().EncryptedSessionToken;
        if (token is null)
        {
            return false;
        }

        try
        {
            var value = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(token), null, DataProtectionScope.CurrentUser));
            _cookieContainer.Add(new Uri(_baseUrl), new Cookie(SessionCookieName, value));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    public void ClearSession()
    {
        var state = _stateStore.Load();
        _stateStore.Save(state with { EncryptedSessionToken = null });

        foreach (Cookie cookie in _cookieContainer.GetCookies(new Uri(_baseUrl)))
        {
            cookie.Expired = true;
        }
    }

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var challenge = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SignInTimeout);

        Process.Start(new ProcessStartInfo(
            $"{_baseUrl}/api/auth/desktop?challenge={challenge}&port={port}") { UseShellExecute = true })?.Dispose();

        string token;
        try
        {
            token = await WaitForTokenAsync(listener, verifier, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Steward did not hear back from the browser.");
        }
        finally
        {
            listener.Stop();
        }

        _cookieContainer.Add(new Uri(_baseUrl), new Cookie(SessionCookieName, token));

        var protectedToken = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        var state = _stateStore.Load();
        _stateStore.Save(state with { EncryptedSessionToken = Convert.ToBase64String(protectedToken) });
    }

    private async Task<string> WaitForTokenAsync(
        TcpListener listener,
        string verifier,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var requestLine = await RespondAsync(client, cancellationToken).ConfigureAwait(false);

            if (!LoopbackCallback.TryReadCode(requestLine, out var code))
            {
                continue;
            }

            try
            {
                return await _gigagrugClient
                    .ExchangeDesktopCodeAsync(code, verifier, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static async Task<string?> RespondAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        var response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(CallbackBody)}\r\n" +
            "Connection: close\r\n" +
            "\r\n" +
            CallbackBody;
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        return requestLine;
    }
}
