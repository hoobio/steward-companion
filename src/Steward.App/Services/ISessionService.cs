namespace Steward.App.Services;

public interface ISessionService
{
    event Action<string?>? PendingSignInUrlChanged;

    event Action<bool>? BrowserLaunchAttempted;

    bool TryRestoreSession();

    Task SignInAsync(CancellationToken cancellationToken);

    bool TryOpenPendingSignInUrl();

    void ClearSession();
}
