using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Steward.App.Views;

public sealed partial class SignInWindow : Window
{
    private readonly TaskCompletionSource<CoreWebView2Cookie?> _sessionCookieFound = new();
    private readonly string _baseUrl;
    private readonly string _sessionCookieName;

    public SignInWindow(CoreWebView2Environment environment, string baseUrl, string sessionCookieName)
    {
        InitializeComponent();
        AppWindow.SetIcon(App.IconPath);
        _baseUrl = baseUrl;
        _sessionCookieName = sessionCookieName;
        _ = InitializeWebViewAsync(environment);
    }

    private async Task InitializeWebViewAsync(CoreWebView2Environment environment)
    {
        await WebView.EnsureCoreWebView2Async(environment);
        WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        WebView.CoreWebView2.Navigate($"{_baseUrl}/api/auth/login");
    }

    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        var cookies = await sender.CookieManager.GetCookiesAsync(_baseUrl);
        var sessionCookie = cookies.FirstOrDefault(c => c.Name == _sessionCookieName);
        if (sessionCookie is not null)
        {
            _sessionCookieFound.TrySetResult(sessionCookie);
        }
    }

    public async Task<CoreWebView2Cookie?> WaitForSessionCookieAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var delayTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(_sessionCookieFound.Task, delayTask);
        return completed == _sessionCookieFound.Task ? await _sessionCookieFound.Task : null;
    }
}
