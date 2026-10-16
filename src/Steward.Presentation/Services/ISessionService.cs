namespace Steward.App.Services;

public interface ISessionService
{
    bool TryRestoreSession();

    Task SignInAsync(IProgress<string> signInUrl, CancellationToken cancellationToken);

    bool TryOpenBrowser(string url);

    void ClearSession();
}
