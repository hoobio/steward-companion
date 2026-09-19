using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using Steward.App.Views;
using Steward.Core;

using Microsoft.Web.WebView2.Core;

namespace Steward.App.Services;

public sealed class SessionService : ISessionService
{
    private const string SessionCookieName = "gg_session";
    private const uint ErrorFileNotFoundHResult = 0x80070002;

    private readonly CookieContainer _cookieContainer;
    private readonly AppStateStore _stateStore;
    private readonly string _baseUrl;

    public SessionService(CookieContainer cookieContainer, AppStateStore stateStore, string baseUrl)
    {
        _cookieContainer = cookieContainer;
        _stateStore = stateStore;
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
    }

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString(null);
        }
        // The WinUI 3 WebView2 projection surfaces a missing Evergreen Runtime as a bare
        // COMException (ERROR_FILE_NOT_FOUND), not the classic WebView2RuntimeNotFoundException.
        catch (COMException ex) when ((uint)ex.HResult == ErrorFileNotFoundHResult)
        {
            throw new InvalidOperationException(
                "The WebView2 Evergreen Runtime is not installed. Install it from " +
                "https://developer.microsoft.com/microsoft-edge/webview2/ and restart Steward.",
                ex);
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Steward",
            "WebView2");

        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);

        var window = new SignInWindow(environment, _baseUrl, SessionCookieName);
        try
        {
            var cookie = await window.WaitForSessionCookieAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (cookie is null)
            {
                window.Activate();
                cookie = await window.WaitForSessionCookieAsync(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (cookie is null)
            {
                throw new InvalidOperationException("Sign-in was cancelled before a session cookie was issued.");
            }

            _cookieContainer.Add(new Uri(_baseUrl), new Cookie(cookie.Name, cookie.Value));

            // gigagrug's Set-Cookie Max-Age is a fixed 30 days, but the server actually slides the
            // session to 90 days from last use and only the raw token (not the cookie) can outlive
            // that 30-day mark once the app supplies it itself instead of depending on WebView2.
            var protectedToken = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(cookie.Value), null, DataProtectionScope.CurrentUser);
            var state = _stateStore.Load();
            _stateStore.Save(state with { EncryptedSessionToken = Convert.ToBase64String(protectedToken) });
        }
        finally
        {
            window.Close();
        }
    }
}
