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
    private const string CallbackIcon =
        "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";

    private const string CallbackBody =
        "<!doctype html><html><head><title>Steward</title>" +
        "<link rel=\"icon\" href=\"data:image/png;base64," + CallbackIcon + "\">" +
        "<style>" +
        ":root{color-scheme:dark;--bg:#1f2430;--surface:#232834;--text:#cbccc6;--text-dim:#8a9199;--panel:8px}" +
        "*{margin:0;box-sizing:border-box}" +
        "body{min-height:100vh;display:flex;align-items:center;justify-content:center;background:var(--bg);" +
        "font-family:'Hanken Grotesk',ui-sans-serif,system-ui,-apple-system,'Segoe UI',Roboto,'Helvetica Neue',sans-serif;color:var(--text)}" +
        ".card{display:flex;flex-direction:column;align-items:center;gap:16px;padding:32px 40px;" +
        "background:var(--surface);border-radius:var(--panel);text-align:center}" +
        "img{width:48px;height:48px}" +
        "h1{font-size:1.25rem;font-weight:600;margin:0}" +
        "p{font-size:1rem;color:var(--text-dim);margin:0}" +
        "button{height:32px;padding:0 16px;border:none;border-radius:4px;background:#409fff;" +
        "color:#ffffff;font:inherit;font-weight:600;cursor:pointer}" +
        "button:hover{background:#73d0ff}" +
        "</style></head><body>" +
        "<div class=\"card\">" +
        "<img src=\"data:image/png;base64," + CallbackIcon + "\" alt=\"Steward\">" +
        "<h1>Signed in to Steward</h1>" +
        "<p>You can close this tab and return to the app.</p>" +
        "<button id=\"closeBtn\">Close tab</button>" +
        "<p id=\"closeHint\" hidden>Your browser kept this tab open. Close it with " +
        "<span id=\"closeKey\">Ctrl+W</span>.</p>" +
        "<script>" +
        "if(/Mac/.test(navigator.platform)){document.getElementById('closeKey').textContent='Cmd+W'}" +
        "try{window.close()}catch(e){}" +
        "document.getElementById('closeBtn').addEventListener('click',function(){" +
        "try{window.close()}catch(e){}" +
        "setTimeout(function(){" +
        "document.getElementById('closeBtn').hidden=true;" +
        "document.getElementById('closeHint').hidden=false" +
        "},300)" +
        "});" +
        "</script>" +
        "</div></body></html>";

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
            throw new TimeoutException("No sign-in reached Steward from the browser.");
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
